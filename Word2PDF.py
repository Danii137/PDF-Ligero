import ctypes
import msvcrt
import os
import queue
import socket
import sys
import threading
import time

import pythoncom
import win32com.client
from PyQt5.QtCore import Qt
from PyQt5.QtGui import QBrush, QColor, QIcon, QLinearGradient, QPainter
from PyQt5.QtWidgets import (
    QApplication,
    QFileDialog,
    QLabel,
    QMainWindow,
    QMessageBox,
    QPushButton,
)

HOST = "127.0.0.1"
PORT = 65432
IDLE_TIMEOUT_SECONDS = 3.0
LOG_FILE = os.path.join(os.path.expanduser("~"), "Desktop", "word2pdf_log.txt")
ALLOWED_EXTENSIONS = {".doc", ".docx", ".rtf"}
PDF_FILE_FORMAT = 17
MAX_BATCH_FILES = 50

# NO cambiar la impresora activa de Word para acelerar la exportacion.
#
# Word pagina usando el driver de la impresora activa, asi que la primera
# exportacion espera a que ese driver responda. Con una impresora de red en
# puerto WSD eso puede tardar unos once segundos, frente a las centesimas que
# tardan las siguientes. Apuntar Word a un driver local baja ese primer PDF a
# menos de dos segundos, y se comprobo que el resultado es identico byte a byte.
#
# Aun asi no se hace, por dos motivos medidos:
#
# 1. Application.ActivePrinter no es un ajuste de Word: cambia la impresora
#    predeterminada de Windows del usuario. Si el proceso muere a medias, la
#    persona se queda imprimiendo a otro sitio sin saberlo.
# 2. Devolver la impresora original cuesta casi lo mismo que se ahorra (7,8 s
#    medidos), porque volver a asignarla inicializa su driver igual. El coste se
#    mueve del principio al final en vez de desaparecer.
#
# La causa real es el puerto WSD de la impresora, y se arregla instalandola en un
# puerto TCP/IP estandar. Eso acelera Word entero, no solo esta herramienta.

APP_NAME = "PDF Ligero"
CONSOLE_TITLE = "PDF Ligero - Convertir a PDF"
ICON_FILE = "PDFLigero.ico"

# Icono de la ventanita de conversion. Es el Homer de siempre, y va aparte del
# icono de la aplicacion: el platano rojo identifica la herramienta en el
# Explorador y en el menu contextual; Homer sale mientras se convierte.
CONSOLE_ICON_FILE = "homer.ico"

FAREWELL_MESSAGE = "Fin. Da las gracias a Dani :)"

IMAGE_ICON = 1
LR_LOADFROMFILE = 0x00000010
LR_DEFAULTSIZE = 0x00000040
WM_SETICON = 0x0080
ICON_SMALL = 0
ICON_BIG = 1

# Windows no se queda con una copia del icono: hay que conservar los handles
# vivos mientras exista la ventana.
_console_icon_handles = []


def resource_path(relative_name):
    """Ruta de un recurso, tanto en desarrollo como dentro del EXE.

    PyInstaller en modo onefile descomprime los datos en una carpeta temporal
    cuya ruta deja en sys._MEIPASS.
    """
    base = getattr(sys, "_MEIPASS", os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(base, relative_name)


def application_icon():
    """Icono compartido con PDF Ligero, o None si no se empaquetó."""
    icon_path = resource_path(ICON_FILE)
    if os.path.exists(icon_path):
        return QIcon(icon_path)

    return None


def apply_console_icon():
    """Pone a Homer en la ventana de conversion.

    Si el icono no viene empaquetado, la ventana se queda con el icono normal:
    no es motivo para dejar de convertir.
    """
    icon_path = resource_path(CONSOLE_ICON_FILE)
    if not os.path.exists(icon_path):
        return

    try:
        user32 = ctypes.windll.user32
        console_window = ctypes.windll.kernel32.GetConsoleWindow()
        if not console_window:
            return

        icon_handle = user32.LoadImageW(
            None,
            icon_path,
            IMAGE_ICON,
            0,
            0,
            LR_LOADFROMFILE | LR_DEFAULTSIZE,
        )
        if not icon_handle:
            return

        _console_icon_handles.append(icon_handle)
        user32.SendMessageW(console_window, WM_SETICON, ICON_SMALL, icon_handle)
        user32.SendMessageW(console_window, WM_SETICON, ICON_BIG, icon_handle)
    except Exception:
        pass


def log(message):
    timestamp = time.strftime("%Y-%m-%d %H:%M:%S")
    try:
        with open(LOG_FILE, "a", encoding="utf-8") as handle:
            handle.write(f"[{timestamp}] [{os.getpid()}] {message}\n")
    except OSError:
        pass


def has_console():
    return bool(ctypes.windll.kernel32.GetConsoleWindow())


def ensure_console():
    if has_console():
        return

    if not ctypes.windll.kernel32.AllocConsole():
        return

    sys.stdout = open("CONOUT$", "w", encoding="utf-8", buffering=1)
    sys.stderr = open("CONOUT$", "w", encoding="utf-8", buffering=1)
    sys.stdin = open("CONIN$", "r", encoding="utf-8", errors="ignore")
    ctypes.windll.kernel32.SetConsoleTitleW(CONSOLE_TITLE)
    apply_console_icon()


def pause_console():
    if not has_console():
        return

    print("")
    print(FAREWELL_MESSAGE)
    print("Pulsa una tecla para cerrar...")
    try:
        msvcrt.getch()
    except Exception:
        os.system("pause")


def normalize_candidates(paths):
    normalized = []
    seen = set()

    for raw_path in paths:
        if not raw_path:
            continue

        path = os.path.abspath(os.path.normpath(raw_path.strip().strip('"')))
        if not os.path.exists(path):
            log(f"Ruta inexistente ignorada: {path}")
            continue

        extension = os.path.splitext(path)[1].lower()
        if extension not in ALLOWED_EXTENSIONS:
            log(f"Extension no soportada ignorada: {path}")
            continue

        if path in seen:
            continue

        normalized.append(path)
        seen.add(path)

    return normalized


class WordPdfConverter:
    """Mantiene una sola instancia de Word para todo el lote.

    Antes se abria y se cerraba Word por cada archivo, asi que convertir veinte
    documentos arrancaba Word veinte veces. Con una sola instancia compartida el
    lote va mucho mas rapido y Word deja de parpadear en la barra de tareas.
    """

    def __init__(self):
        self.word_app = None
        self.com_initialized = False

    def open(self):
        if self.word_app is not None:
            return

        pythoncom.CoInitialize()
        self.com_initialized = True
        try:
            self.word_app = win32com.client.DispatchEx("Word.Application")
            self.word_app.Visible = False
            self.word_app.DisplayAlerts = 0
            self.word_app.ScreenUpdating = False
        except Exception:
            if self.com_initialized:
                pythoncom.CoUninitialize()
                self.com_initialized = False
            raise

    def close(self):
        if self.word_app is not None:
            try:
                self.word_app.Quit()
            except Exception:
                pass
            finally:
                self.word_app = None

        if self.com_initialized:
            pythoncom.CoUninitialize()
            self.com_initialized = False

    def convert_file(self, input_file, output_file):
        self.open()

        document = None
        try:
            # ExportAsFixedFormat es la exportacion a PDF de verdad. SaveAs con
            # FileFormat=17 tambien genera un PDF, pero marca el documento como
            # modificado y respeta peor la maquetacion.
            document = self.word_app.Documents.Open(
                os.path.abspath(input_file),
                ReadOnly=True,
                AddToRecentFiles=False,
                ConfirmConversions=False,
                NoEncodingDialog=True,
            )
            document.ExportAsFixedFormat(
                os.path.abspath(output_file),
                PDF_FILE_FORMAT,
            )
        finally:
            if document is not None:
                try:
                    document.Close(False)
                except Exception:
                    pass


def convert_single_file(input_file, converter=None):
    output_dir = os.path.dirname(input_file)
    pdf_filename = os.path.splitext(os.path.basename(input_file))[0] + ".pdf"
    output_file = os.path.join(output_dir, pdf_filename)

    log(f"Convirtiendo: {input_file} -> {output_file}")

    own_converter = converter is None
    active = converter or WordPdfConverter()
    try:
        active.convert_file(input_file, output_file)
    finally:
        if own_converter:
            active.close()

    return output_file


def show_error_dialog(title, message):
    try:
        ctypes.windll.user32.MessageBoxW(0, message, title, 0x10)
    except Exception:
        pass


def validate_batch_size(paths):
    if len(paths) <= MAX_BATCH_FILES:
        return True

    message = (
        f"Has seleccionado {len(paths)} archivos.\n\n"
        f"El maximo permitido por lote es {MAX_BATCH_FILES} para no saturar el equipo.\n"
        "Reduce la seleccion y vuelve a intentarlo."
    )
    print(message)
    log(f"Lote rechazado por exceso de archivos: {len(paths)}")
    show_error_dialog(APP_NAME, message)
    return False


def print_summary(successful, failed):
    print("")
    print("=== Resumen ===")
    print(f"Procesados: {len(successful) + len(failed)}")
    print(f"Correctos: {len(successful)}")
    print(f"Errores: {len(failed)}")
    print("")
    print("Archivos convertidos:")
    if successful:
        for path in successful:
            print(f"  OK  {os.path.basename(path)}")
    else:
        print("  Ninguno")

    print("")
    print("Archivos con error:")
    if failed:
        for path, error in failed:
            print(f"  ERROR  {os.path.basename(path)} -> {error}")
    else:
        print("  Ninguno")

    print("")
    print(f"Log: {LOG_FILE}")

    if successful and not failed:
        print("")
        print("Todos los archivos se han convertido correctamente.")


def run_cli(paths):
    initial_files = normalize_candidates(paths)
    if not initial_files:
        show_error_dialog(APP_NAME, "No hay archivos DOC, DOCX o RTF validos para convertir.")
        log("No se recibieron archivos validos.")
        return 1
    if not validate_batch_size(initial_files):
        return 1

    server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    is_server = False

    try:
        server_socket.bind((HOST, PORT))
        server_socket.listen()
        is_server = True
    except OSError:
        server_socket.close()

    if not is_server:
        try:
            payload = "\n".join(initial_files).encode("utf-8")
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as client_socket:
                client_socket.connect((HOST, PORT))
                client_socket.sendall(payload)
            return 0
        except OSError:
            log("No fue posible conectar con la instancia principal. Se procesara localmente.")
            ensure_console()
            return process_queue(initial_files, server_socket=None)

    ensure_console()
    return process_queue(initial_files, server_socket=server_socket)


def process_queue(initial_files, server_socket=None):
    file_queue = queue.Queue()
    queued_paths = set()

    def enqueue_many(paths):
        for path in normalize_candidates(paths):
            if path in queued_paths:
                continue
            file_queue.put(path)
            queued_paths.add(path)

    enqueue_many(initial_files)

    if server_socket is not None:

        def connection_listener():
            while True:
                try:
                    conn, _ = server_socket.accept()
                except OSError:
                    break

                with conn:
                    try:
                        data = conn.recv(65536)
                    except OSError:
                        continue

                    if not data:
                        continue

                    payload = data.decode("utf-8", errors="ignore")
                    enqueue_many(payload.splitlines())

        listener = threading.Thread(target=connection_listener, daemon=True)
        listener.start()

    print("=== Conversor Word a PDF ===")
    print("Procesando archivos...")
    print("")

    successful = []
    failed = []
    last_activity = time.time()
    # Una sola instancia de Word para todo el lote, incluidos los archivos que
    # lleguen despues por el socket.
    converter = WordPdfConverter()

    try:
        while True:
            try:
                current_file = file_queue.get(timeout=0.5)
            except queue.Empty:
                if time.time() - last_activity > IDLE_TIMEOUT_SECONDS:
                    break
                continue

            try:
                output_file = convert_single_file(current_file, converter)
                successful.append(output_file)
                print(f"OK: {os.path.basename(current_file)} -> {os.path.basename(output_file)}")
            except Exception as exc:
                failed.append((current_file, str(exc)))
                print(f"ERROR: {os.path.basename(current_file)} -> {exc}")
                log(f"ERROR convirtiendo {current_file}: {exc}")
            finally:
                file_queue.task_done()
                last_activity = time.time()

        # El resumen se muestra antes de cerrar Word: los PDF ya estan escritos
        # en este punto, y cerrar Word puede llevarse varios segundos. Asi se ve
        # el resultado en cuanto existe, no cuando termina la limpieza.
        print_summary(successful, failed)
    finally:
        converter.close()
        if server_socket is not None:
            server_socket.close()

    log(f"Proceso finalizado. Correctos={len(successful)} Fallos={len(failed)}")

    if failed:
        failed_names = "\n".join(os.path.basename(path) for path, _ in failed[:10])
        show_error_dialog(
            APP_NAME,
            "Algunos archivos no se pudieron convertir.\n\n"
            f"Revisa {LOG_FILE}\n\n"
            f"Archivos con error:\n{failed_names}",
        )

    pause_console()
    return 0 if not failed else 1


class DocToPdfConverter(QMainWindow):
    def __init__(self):
        super().__init__()
        self.uploaded_files = []
        self.output_directory = ""
        self.drag_position = None

        self.setWindowFlags(Qt.FramelessWindowHint | Qt.Window)
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setGeometry(100, 100, 420, 410)
        self.setWindowTitle("Convertir a PDF - " + APP_NAME)

        icon = application_icon()
        if icon is not None:
            self.setWindowIcon(icon)

        self.create_interface()

    def create_interface(self):
        self.title_label = QLabel("Convertir a PDF", self)
        self.title_label.setGeometry(40, 20, 340, 60)
        self.title_label.setStyleSheet(
            "font-size: 24px; font-weight: lighter; color: #E0E0E0; font-family: 'Segoe UI';"
        )
        self.title_label.setAlignment(Qt.AlignCenter)

        self.minimize_button = QPushButton("_", self)
        self.minimize_button.setGeometry(360, 12, 25, 20)
        self.minimize_button.setStyleSheet(
            "border: none; background-color: transparent; color: #E0E0E0; font-size: 16px;"
        )
        self.minimize_button.clicked.connect(self.showMinimized)

        self.close_button = QPushButton("X", self)
        self.close_button.setGeometry(388, 12, 25, 20)
        self.close_button.setStyleSheet(
            "border: none; background-color: transparent; color: #E0E0E0; font-size: 16px;"
        )
        self.close_button.clicked.connect(self.close)

        self.file_button = QPushButton("Seleccionar archivos", self)
        self.file_button.setGeometry(110, 100, 200, 50)
        self.file_button.setStyleSheet(self.button_style())
        self.file_button.clicked.connect(self.select_files)

        self.file_label = QLabel("Ningun archivo seleccionado", self)
        self.file_label.setGeometry(35, 170, 350, 45)
        self.file_label.setStyleSheet("color: #E0E0E0; font-size: 14px; font-family: 'Segoe UI';")
        self.file_label.setAlignment(Qt.AlignCenter)
        self.file_label.setWordWrap(True)

        self.output_button = QPushButton("Seleccionar carpeta destino", self)
        self.output_button.setGeometry(110, 230, 200, 50)
        self.output_button.setStyleSheet(self.button_style())
        self.output_button.clicked.connect(self.select_output_dir)

        self.output_label = QLabel("Ninguna carpeta seleccionada", self)
        self.output_label.setGeometry(35, 295, 350, 45)
        self.output_label.setStyleSheet("color: #E0E0E0; font-size: 14px; font-family: 'Segoe UI';")
        self.output_label.setAlignment(Qt.AlignCenter)
        self.output_label.setWordWrap(True)

        self.convert_button = QPushButton("Convertir archivos", self)
        self.convert_button.setGeometry(110, 350, 200, 45)
        self.convert_button.setStyleSheet(self.button_style())
        self.convert_button.clicked.connect(self.convert_files)

    def button_style(self):
        return """
            QPushButton {
                background-color: rgba(211, 211, 211, 102);
                border: none;
                border-radius: 22px;
                font-size: 14px;
                font-family: 'Segoe UI';
            }
            QPushButton:hover {
                background-color: rgba(191, 191, 191, 102);
            }
            QPushButton:pressed {
                background-color: rgba(169, 169, 169, 102);
            }
        """

    def select_files(self):
        files, _ = QFileDialog.getOpenFileNames(
            self,
            "Seleccionar archivos Word o RTF",
            "",
            "Documentos (*.docx *.doc *.rtf)",
        )
        if not files:
            return

        selected_files = normalize_candidates(self.uploaded_files + files)
        if len(selected_files) > MAX_BATCH_FILES:
            QMessageBox.warning(
                self,
                "Limite de archivos",
                f"Solo se permiten {MAX_BATCH_FILES} archivos por lote para no saturar el equipo.",
            )
            return

        self.uploaded_files = selected_files
        self.file_label.setText(f"{len(self.uploaded_files)} archivo(s) seleccionado(s)")

    def select_output_dir(self):
        directory = QFileDialog.getExistingDirectory(self, "Seleccionar carpeta destino")
        if directory:
            self.output_directory = directory
            self.output_label.setText(directory)

    def convert_files(self):
        if not self.uploaded_files:
            QMessageBox.warning(self, "Error", "Selecciona al menos un archivo DOC, DOCX o RTF.")
            return
        if len(self.uploaded_files) > MAX_BATCH_FILES:
            QMessageBox.warning(
                self,
                "Limite de archivos",
                f"Solo se permiten {MAX_BATCH_FILES} archivos por lote para no saturar el equipo.",
            )
            return

        if not self.output_directory:
            QMessageBox.warning(self, "Error", "Selecciona una carpeta de destino.")
            return

        success_count = 0
        failed_files = []

        for source_file in self.uploaded_files:
            try:
                pdf_file = os.path.join(
                    self.output_directory,
                    os.path.splitext(os.path.basename(source_file))[0] + ".pdf",
                )
                convert_with_word(source_file, pdf_file)
                success_count += 1
            except Exception as exc:
                failed_files.append(f"{os.path.basename(source_file)} ({exc})")
                log(f"ERROR GUI convirtiendo {source_file}: {exc}")

        message = f"Se han convertido {success_count} de {len(self.uploaded_files)} archivos."
        if failed_files:
            message += "\n\nErrores:\n" + "\n".join(failed_files[:10])

        QMessageBox.information(self, "Proceso completado", message)

    def paintEvent(self, _event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing)
        gradient = QLinearGradient(0, 0, 0, self.height())
        gradient.setColorAt(0.0, QColor(18, 35, 42, 230))
        gradient.setColorAt(0.2, QColor(47, 58, 50, 230))
        gradient.setColorAt(0.4, QColor(84, 87, 72, 230))
        gradient.setColorAt(0.6, QColor(219, 159, 117, 230))
        gradient.setColorAt(0.8, QColor(128, 64, 18, 230))
        gradient.setColorAt(1.0, QColor(62, 36, 17, 230))
        painter.setBrush(QBrush(gradient))
        painter.setPen(Qt.NoPen)
        painter.drawRoundedRect(0, 0, self.width(), self.height(), 20, 20)

    def mousePressEvent(self, event):
        if event.button() == Qt.LeftButton:
            self.drag_position = event.globalPos() - self.frameGeometry().topLeft()

    def mouseMoveEvent(self, event):
        if event.buttons() == Qt.LeftButton and self.drag_position is not None:
            self.move(event.globalPos() - self.drag_position)


def main():
    if len(sys.argv) > 1:
        return run_cli(sys.argv[1:])

    app = QApplication(sys.argv)
    app.setApplicationName(APP_NAME)
    icon = application_icon()
    if icon is not None:
        app.setWindowIcon(icon)

    window = DocToPdfConverter()
    window.show()
    return app.exec_()


if __name__ == "__main__":
    sys.exit(main())
