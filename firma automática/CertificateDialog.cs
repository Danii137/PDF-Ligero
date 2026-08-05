using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;

namespace FirmaAutomatica
{
    internal sealed class CertificateDialog : Form
    {
        private const string StoreSource = "store";
        private const string FileSource = "file";
        private readonly TabControl sourceTabs;
        private readonly TabPage windowsStoreTab;
        private readonly TabPage fileTab;
        private readonly ListBox storeCertificatesListBox;
        private readonly TextBox storeCertificateDetailsTextBox;
        private readonly TextBox certificatePathTextBox;
        private readonly TextBox passwordTextBox;
        private readonly GroupBox signatureAppearanceGroupBox;
        private readonly PictureBox signaturePreviewPictureBox;
        private readonly Label signatureStatusLabel;
        private readonly Button chooseSignatureGraphicButton;
        private readonly Button resetSignatureGraphicButton;
        private readonly List<CertificateListItem> availableStoreCertificates = new List<CertificateListItem>();
        private readonly CertificateSelectionPreferences selectionPreferences;
        private string pendingFileCertificateGraphicPath;
        private bool resetFileCertificateGraphicOnAccept;

        public CertificateDialog()
        {
            selectionPreferences = UserPreferences.LoadCertificateSelection();
            Text = "Seleccionar certificado";
            AppBranding.ApplyWindowIcon(this);
            Width = 760;
            Height = 590;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var introLabel = new Label
            {
                Left = 16,
                Top = 14,
                Width = 710,
                Height = 34,
                Text = "Elige un certificado instalado en Windows o, si prefieres, carga un archivo .pfx/.p12."
            };

            sourceTabs = new TabControl
            {
                Left = 16,
                Top = 52,
                Width = 710,
                Height = 350
            };

            windowsStoreTab = new TabPage("Windows Personal");
            fileTab = new TabPage("Archivo PFX/P12");

            var storeHelpLabel = new Label
            {
                Left = 12,
                Top = 14,
                Width = 660,
                Height = 18,
                Text = "Se muestran los certificados del almacen Personal con clave privada y no caducados."
            };

            storeCertificatesListBox = new ListBox
            {
                Left = 12,
                Top = 40,
                Width = 660,
                Height = 160
            };
            storeCertificatesListBox.SelectedIndexChanged += StoreCertificatesListBox_SelectedIndexChanged;

            var refreshButton = new Button
            {
                Left = 572,
                Top = 208,
                Width = 100,
                Height = 28,
                Text = "Recargar"
            };
            refreshButton.Click += RefreshButton_Click;

            var detailsLabel = new Label
            {
                Left = 12,
                Top = 214,
                Width = 180,
                Height = 18,
                Text = "Detalles del certificado"
            };

            storeCertificateDetailsTextBox = new TextBox
            {
                Left = 12,
                Top = 240,
                Width = 660,
                Height = 72,
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };

            windowsStoreTab.Controls.Add(storeHelpLabel);
            windowsStoreTab.Controls.Add(storeCertificatesListBox);
            windowsStoreTab.Controls.Add(refreshButton);
            windowsStoreTab.Controls.Add(detailsLabel);
            windowsStoreTab.Controls.Add(storeCertificateDetailsTextBox);

            var fileHelpLabel = new Label
            {
                Left = 12,
                Top = 20,
                Width = 660,
                Height = 18,
                Text = "Usa esta opcion solo si no quieres seleccionar un certificado ya instalado en Windows."
            };

            var pathLabel = new Label
            {
                Left = 12,
                Top = 64,
                Width = 660,
                Height = 18,
                Text = "Certificado (.pfx o .p12)"
            };

            certificatePathTextBox = new TextBox
            {
                Left = 12,
                Top = 88,
                Width = 560
            };

            var browseButton = new Button
            {
                Left = 580,
                Top = 86,
                Width = 92,
                Height = 26,
                Text = "Buscar"
            };
            browseButton.Click += BrowseButton_Click;

            var passwordLabel = new Label
            {
                Left = 12,
                Top = 130,
                Width = 660,
                Height = 18,
                Text = "Contrasena del certificado"
            };

            passwordTextBox = new TextBox
            {
                Left = 12,
                Top = 154,
                Width = 660,
                UseSystemPasswordChar = true
            };

            fileTab.Controls.Add(fileHelpLabel);
            fileTab.Controls.Add(pathLabel);
            fileTab.Controls.Add(certificatePathTextBox);
            fileTab.Controls.Add(browseButton);
            fileTab.Controls.Add(passwordLabel);
            fileTab.Controls.Add(passwordTextBox);

            sourceTabs.TabPages.Add(windowsStoreTab);
            sourceTabs.TabPages.Add(fileTab);
            sourceTabs.SelectedIndexChanged += SourceTabs_SelectedIndexChanged;

            signatureAppearanceGroupBox = new GroupBox
            {
                Left = 16,
                Top = 408,
                Width = 710,
                Height = 92,
                Text = "Firma visual asociada al certificado"
            };

            signaturePreviewPictureBox = new PictureBox
            {
                Left = 12,
                Top = 22,
                Width = 124,
                Height = 56,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom
            };

            signatureStatusLabel = new Label
            {
                Left = 148,
                Top = 22,
                Width = 304,
                Height = 56,
                AutoEllipsis = true,
                Text = "Selecciona un certificado para configurar su firma visual."
            };

            chooseSignatureGraphicButton = new Button
            {
                Left = 466,
                Top = 28,
                Width = 118,
                Height = 30,
                Text = "Elegir imagen..."
            };
            chooseSignatureGraphicButton.Click += ChooseSignatureGraphicButton_Click;

            resetSignatureGraphicButton = new Button
            {
                Left = 594,
                Top = 28,
                Width = 100,
                Height = 30,
                Text = "Predeterminada"
            };
            resetSignatureGraphicButton.Click += ResetSignatureGraphicButton_Click;

            signatureAppearanceGroupBox.Controls.Add(signaturePreviewPictureBox);
            signatureAppearanceGroupBox.Controls.Add(signatureStatusLabel);
            signatureAppearanceGroupBox.Controls.Add(chooseSignatureGraphicButton);
            signatureAppearanceGroupBox.Controls.Add(resetSignatureGraphicButton);
            certificatePathTextBox.TextChanged += CertificatePathTextBox_TextChanged;

            var acceptButton = new Button
            {
                Left = 520,
                Top = 514,
                Width = 96,
                Height = 30,
                Text = "Continuar"
            };
            acceptButton.Click += AcceptButton_Click;

            var cancelButton = new Button
            {
                Left = 628,
                Top = 514,
                Width = 96,
                Height = 30,
                Text = "Cancelar",
                DialogResult = DialogResult.Cancel
            };

            Controls.Add(introLabel);
            Controls.Add(sourceTabs);
            Controls.Add(signatureAppearanceGroupBox);
            Controls.Add(acceptButton);
            Controls.Add(cancelButton);

            AcceptButton = acceptButton;
            CancelButton = cancelButton;
            Load += CertificateDialog_Load;
            FormClosed += CertificateDialog_FormClosed;
        }

        public X509Certificate2 SelectedCertificate { get; private set; }

        public string SelectedCertificateLabel { get; private set; }

        private void CertificateDialog_Load(object sender, EventArgs e)
        {
            LoadWindowsStoreCertificates();
            RestoreLastSelection();
            UpdateSignatureAppearanceUi();
        }

        private void CertificateDialog_FormClosed(object sender, FormClosedEventArgs e)
        {
            SetSignaturePreview(null);
        }

        private void SourceTabs_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateSignatureAppearanceUi();
        }

        private void CertificatePathTextBox_TextChanged(object sender, EventArgs e)
        {
            pendingFileCertificateGraphicPath = null;
            resetFileCertificateGraphicOnAccept = false;
            UpdateSignatureAppearanceUi();
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            LoadWindowsStoreCertificates();
        }

        private void StoreCertificatesListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            var item = storeCertificatesListBox.SelectedItem as CertificateListItem;
            storeCertificateDetailsTextBox.Text = item == null ? string.Empty : item.Details;
            UpdateSignatureAppearanceUi();
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "Certificados (*.pfx;*.p12)|*.pfx;*.p12|Todos los archivos (*.*)|*.*";
                dialog.Title = "Selecciona el certificado";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    certificatePathTextBox.Text = dialog.FileName;
                }
            }
        }

        private void ChooseSignatureGraphicButton_Click(object sender, EventArgs e)
        {
            if (sourceTabs.SelectedTab == windowsStoreTab && storeCertificatesListBox.SelectedItem == null)
            {
                MessageBox.Show(this, "Selecciona primero un certificado de Windows.", "Firmar PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "Imagenes de firma (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos los archivos (*.*)|*.*";
                dialog.Title = "Selecciona la imagen de la firma";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                Image previewImage = null;
                try
                {
                    previewImage = LoadPreviewImage(dialog.FileName);
                    if (previewImage == null)
                    {
                        throw new InvalidDataException("El archivo no contiene una imagen valida.");
                    }

                    if (sourceTabs.SelectedTab == windowsStoreTab)
                    {
                        var item = storeCertificatesListBox.SelectedItem as CertificateListItem;
                        UserPreferences.SaveCertificateSignatureGraphic(item.Certificate.Thumbprint, dialog.FileName);
                    }
                    else
                    {
                        pendingFileCertificateGraphicPath = dialog.FileName;
                        resetFileCertificateGraphicOnAccept = false;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "No se pudo usar la imagen seleccionada: " + ex.Message, "Firmar PDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                finally
                {
                    if (previewImage != null)
                    {
                        previewImage.Dispose();
                    }
                }
            }

            UpdateSignatureAppearanceUi();
        }

        private void ResetSignatureGraphicButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (sourceTabs.SelectedTab == windowsStoreTab)
                {
                    var item = storeCertificatesListBox.SelectedItem as CertificateListItem;
                    if (item == null)
                    {
                        return;
                    }

                    UserPreferences.ResetCertificateSignatureGraphic(item.Certificate.Thumbprint);
                }
                else
                {
                    pendingFileCertificateGraphicPath = null;
                    resetFileCertificateGraphicOnAccept = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo restablecer la firma predeterminada: " + ex.Message, "Firmar PDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            UpdateSignatureAppearanceUi();
        }

        private void AcceptButton_Click(object sender, EventArgs e)
        {
            if (sourceTabs.SelectedTab == windowsStoreTab)
            {
                AcceptWindowsStoreCertificate();
                return;
            }

            AcceptFileCertificate();
        }

        private void AcceptWindowsStoreCertificate()
        {
            var item = storeCertificatesListBox.SelectedItem as CertificateListItem;
            if (item == null)
            {
                MessageBox.Show(this, "Selecciona un certificado del almacen Personal.", "Firmar PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SelectedCertificate = new X509Certificate2(item.Certificate);
            SelectedCertificateLabel = item.DisplayName;
            UserPreferences.SaveCertificateSelection(StoreSource, item.Certificate.Thumbprint, certificatePathTextBox.Text.Trim());
            DialogResult = DialogResult.OK;
            Close();
        }

        private void AcceptFileCertificate()
        {
            var path = certificatePathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show(this, "Selecciona un certificado valido.", "Firmar PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var certificate = new X509Certificate2(
                    path,
                    passwordTextBox.Text,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);

                if (!certificate.HasPrivateKey)
                {
                    MessageBox.Show(this, "El certificado seleccionado no tiene clave privada.", "Firmar PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    certificate.Dispose();
                    return;
                }

                try
                {
                    if (!string.IsNullOrWhiteSpace(pendingFileCertificateGraphicPath))
                    {
                        UserPreferences.SaveCertificateSignatureGraphic(certificate.Thumbprint, pendingFileCertificateGraphicPath);
                    }
                    else if (resetFileCertificateGraphicOnAccept)
                    {
                        UserPreferences.ResetCertificateSignatureGraphic(certificate.Thumbprint);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "No se pudo guardar la firma visual asociada: " + ex.Message, "Firmar PDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    certificate.Dispose();
                    return;
                }

                SelectedCertificate = certificate;
                SelectedCertificateLabel = Path.GetFileName(path);
                UserPreferences.SaveCertificateSelection(FileSource, string.Empty, path);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo abrir el certificado: " + ex.Message, "Firmar PDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateSignatureAppearanceUi()
        {
            if (signatureStatusLabel == null || chooseSignatureGraphicButton == null || resetSignatureGraphicButton == null)
            {
                return;
            }

            if (sourceTabs.SelectedTab == windowsStoreTab)
            {
                var item = storeCertificatesListBox.SelectedItem as CertificateListItem;
                chooseSignatureGraphicButton.Enabled = item != null;
                if (item == null)
                {
                    resetSignatureGraphicButton.Enabled = false;
                    signatureStatusLabel.Text = "Selecciona un certificado para configurar su firma visual.";
                    SetSignaturePreview(null);
                    return;
                }

                var customPath = UserPreferences.GetCertificateSignatureGraphicPath(item.Certificate.Thumbprint);
                if (!string.IsNullOrWhiteSpace(customPath))
                {
                    resetSignatureGraphicButton.Enabled = true;
                    signatureStatusLabel.Text = "Firma personalizada para este certificado." + Environment.NewLine + Path.GetFileName(customPath);
                    if (!SetSignaturePreview(customPath))
                    {
                        signatureStatusLabel.Text = "La firma personalizada no se puede previsualizar. Se usara la predeterminada.";
                        SetSignaturePreview(ResolveDefaultPreviewPath());
                    }

                    return;
                }

                resetSignatureGraphicButton.Enabled = false;
                signatureStatusLabel.Text = "Este certificado usa la firma predeterminada del programa.";
                SetSignaturePreview(ResolveDefaultPreviewPath());
                return;
            }

            chooseSignatureGraphicButton.Enabled = true;
            resetSignatureGraphicButton.Enabled = true;
            if (!string.IsNullOrWhiteSpace(pendingFileCertificateGraphicPath))
            {
                signatureStatusLabel.Text = "Esta imagen se asociara al certificado PFX/P12 al pulsar Continuar.";
                SetSignaturePreview(pendingFileCertificateGraphicPath);
                return;
            }

            if (resetFileCertificateGraphicOnAccept)
            {
                signatureStatusLabel.Text = "Se restablecera la firma predeterminada al pulsar Continuar.";
                SetSignaturePreview(ResolveDefaultPreviewPath());
                return;
            }

            signatureStatusLabel.Text = "La firma guardada no se cambiara. Puedes elegir otra imagen o restablecerla.";
            SetSignaturePreview(null);
        }

        private bool SetSignaturePreview(string path)
        {
            var previousImage = signaturePreviewPictureBox == null ? null : signaturePreviewPictureBox.Image;
            if (signaturePreviewPictureBox != null)
            {
                signaturePreviewPictureBox.Image = null;
            }

            if (previousImage != null)
            {
                previousImage.Dispose();
            }

            if (signaturePreviewPictureBox == null || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                signaturePreviewPictureBox.Image = LoadPreviewImage(path);
                return signaturePreviewPictureBox.Image != null;
            }
            catch
            {
                return false;
            }
        }

        private static Image LoadPreviewImage(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var image = Image.FromStream(stream, false, true))
            {
                return new Bitmap(image);
            }
        }

        private static string ResolveDefaultPreviewPath()
        {
            var candidatePaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "firma_limpia.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "firma.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "signature.png")
            };

            return candidatePaths.FirstOrDefault(File.Exists);
        }

        private void LoadWindowsStoreCertificates()
        {
            availableStoreCertificates.Clear();
            storeCertificatesListBox.Items.Clear();
            storeCertificateDetailsTextBox.Clear();

            foreach (var item in ReadCertificates(StoreLocation.CurrentUser, "Usuario actual"))
            {
                availableStoreCertificates.Add(item);
            }

            foreach (var item in ReadCertificates(StoreLocation.LocalMachine, "Equipo local"))
            {
                if (availableStoreCertificates.All(existing => !string.Equals(existing.Certificate.Thumbprint, item.Certificate.Thumbprint, StringComparison.OrdinalIgnoreCase)))
                {
                    availableStoreCertificates.Add(item);
                }
            }

            foreach (var item in availableStoreCertificates
                .OrderByDescending(item => item.IsCurrentlyValid)
                .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Certificate.NotAfter))
            {
                storeCertificatesListBox.Items.Add(item);
            }

            if (storeCertificatesListBox.Items.Count > 0)
            {
                storeCertificatesListBox.SelectedIndex = 0;
                sourceTabs.SelectedTab = windowsStoreTab;
            }
            else
            {
                sourceTabs.SelectedTab = fileTab;
                storeCertificateDetailsTextBox.Text = "No se han encontrado certificados utilizables en Windows Personal.";
            }
        }

        private static IEnumerable<CertificateListItem> ReadCertificates(StoreLocation location, string locationLabel)
        {
            var items = new List<CertificateListItem>();
            using (var store = new X509Store(StoreName.My, location))
            {
                try
                {
                    store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);
                }
                catch
                {
                    return items;
                }

                foreach (var certificate in store.Certificates)
                {
                    if (!certificate.HasPrivateKey)
                    {
                        continue;
                    }

                    if (certificate.NotAfter < DateTime.Now)
                    {
                        continue;
                    }

                    items.Add(new CertificateListItem(certificate, locationLabel));
                }
            }

            return items;
        }

        private void RestoreLastSelection()
        {
            if (selectionPreferences == null)
            {
                return;
            }

            if (string.Equals(selectionPreferences.Source, FileSource, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(selectionPreferences.FilePath))
                {
                    certificatePathTextBox.Text = selectionPreferences.FilePath;
                }

                sourceTabs.SelectedTab = fileTab;
                return;
            }

            if (string.IsNullOrWhiteSpace(selectionPreferences.StoreThumbprint))
            {
                return;
            }

            var matchingItem = availableStoreCertificates.FirstOrDefault(
                item => string.Equals(item.Certificate.Thumbprint, selectionPreferences.StoreThumbprint, StringComparison.OrdinalIgnoreCase));

            if (matchingItem == null)
            {
                return;
            }

            storeCertificatesListBox.SelectedItem = matchingItem;
            sourceTabs.SelectedTab = windowsStoreTab;
        }

        private sealed class CertificateListItem
        {
            public CertificateListItem(X509Certificate2 certificate, string locationLabel)
            {
                Certificate = new X509Certificate2(certificate);
                LocationLabel = locationLabel;
                DisplayName = BuildDisplayName(certificate);
                Details = BuildDetails(certificate, locationLabel);
            }

            public X509Certificate2 Certificate { get; private set; }

            public string DisplayName { get; private set; }

            public string Details { get; private set; }

            public string LocationLabel { get; private set; }

            public bool IsCurrentlyValid
            {
                get
                {
                    var now = DateTime.Now;
                    return Certificate.NotBefore <= now && Certificate.NotAfter >= now;
                }
            }

            public override string ToString()
            {
                return DisplayName;
            }

            private static string BuildDisplayName(X509Certificate2 certificate)
            {
                var subject = certificate.GetNameInfo(X509NameType.SimpleName, false);
                if (string.IsNullOrWhiteSpace(subject))
                {
                    subject = certificate.Subject;
                }

                return string.Format(
                    "{0}  |  Caduca: {1:dd/MM/yyyy}",
                    subject,
                    certificate.NotAfter);
            }

            private static string BuildDetails(X509Certificate2 certificate, string locationLabel)
            {
                return string.Format(
                    "Ubicacion: {0}{5}Asunto: {1}{5}Emisor: {2}{5}Valido desde: {3:dd/MM/yyyy}{5}Caduca: {4:dd/MM/yyyy}",
                    locationLabel,
                    certificate.Subject,
                    certificate.Issuer,
                    certificate.NotBefore,
                    certificate.NotAfter,
                    Environment.NewLine);
            }
        }
    }
}
