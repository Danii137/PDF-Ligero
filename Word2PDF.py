import ctypes
import os
import queue
import socket
import sys
import threading
import time

import pythoncom
import win32com.client
from PyQt5.QtCore import Qt
from PyQt5.QtGui import QBrush, QColor, QLinearGradient, QPainter
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
    ctypes.windll.kernel32.SetConsoleTitleW("Word2PDF")


def pause_console():
    if not has_console():
        return

    print("")
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


def convert_with_word(input_file, output_file):
    pythoncom.CoInitialize()
    word = None
    document = None
    try:
        word = win32com.client.DispatchEx("Word.Application")
        word.Visible = False
        word.DisplayAlerts = 0
        document = word.Documents.Open(input_file, ReadOnly=True)
        document.SaveAs(output_file, FileFormat=PDF_FILE_FORMAT)
    finally:
        if document is not None:
            try:
                document.Close(False)
            except Exception:
                pass
        if word is not None:
            try:
                word.Quit()
            except Exception:
                pass
        pythoncom.CoUninitialize()


def convert_single_file(input_file):
    output_dir = os.path.dirname(input_file)
    pdf_filename = os.path.splitext(os.path.basename(input_file))[0] + ".pdf"
    output_file = os.path.join(output_dir, pdf_filename)

    log(f"Convirtiendo: {input_file} -> {output_file}")
    convert_with_word(input_file, output_file)
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
    show_error_dialog("Word2PDF", message)
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


def run_cli(paths):
    initial_files = normalize_candidates(paths)
    if not initial_files:
        show_error_dialog("Word2PDF", "No hay archivos DOC, DOCX o RTF validos para convertir.")
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

    try:
        while True:
            try:
                current_file = file_queue.get(timeout=0.5)
            except queue.Empty:
                if time.time() - last_activity > IDLE_TIMEOUT_SECONDS:
                    break
                continue

            try:
                output_file = convert_single_file(current_file)
                successful.append(output_file)
                print(f"OK: {os.path.basename(current_file)} -> {os.path.basename(output_file)}")
            except Exception as exc:
                failed.append((current_file, str(exc)))
                print(f"ERROR: {os.path.basename(current_file)} -> {exc}")
                log(f"ERROR convirtiendo {current_file}: {exc}")
            finally:
                file_queue.task_done()
                last_activity = time.time()
    finally:
        if server_socket is not None:
            server_socket.close()

    print_summary(successful, failed)
    log(f"Proceso finalizado. Correctos={len(successful)} Fallos={len(failed)}")

    if failed:
        failed_names = "\n".join(os.path.basename(path) for path, _ in failed[:10])
        show_error_dialog(
            "Word2PDF",
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
        self.setWindowTitle("Conversor de Word/RTF a PDF")

        self.create_interface()

    def create_interface(self):
        self.title_label = QLabel("Conversor de Word/RTF a PDF", self)
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
    window = DocToPdfConverter()
    window.show()
    return app.exec_()


if __name__ == "__main__":
    sys.exit(main())
