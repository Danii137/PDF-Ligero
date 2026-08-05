using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using PdfiumViewer;

namespace FirmaAutomatica
{
    /// <summary>
    /// Lightweight, memory-only measuring layer for PdfiumViewer.
    /// Measurements use native PDF coordinates and never modify the document.
    /// </summary>
    internal sealed class PdfMeasurementController :
        IMessageFilter,
        IDisposable
    {
        private const int WmKeyDown = 0x0100;
        private const int WmLButtonDown = 0x0201;
        private const int WmLButtonDoubleClick = 0x0203;
        private const int MaximumMeasurements = 200;
        private const int MaximumVertices = 100;
        private const double DuplicatePointTolerance = 1.5D;

        private readonly PdfRenderer renderer;
        private readonly Func<bool> canActivate;
        private readonly Color accentColor;
        private readonly Color accentTextColor;
        private readonly Color surfaceColor;
        private readonly List<PdfPageMeasurement> measurements;
        private readonly ReadOnlyCollection<PdfPageMeasurement>
            readOnlyMeasurements;
        private readonly Dictionary<int, PdfMeasurementCalibration>
            pageCalibrations;
        private readonly Dictionary<
            PdfPageMeasurement,
            PdfMeasurementCalibration> measurementCalibrations;
        private readonly Dictionary<int, MeasurementPageMarker> pageMarkers;
        private readonly List<PdfMeasurementPoint> draftPoints;
        private readonly Panel toolbar;
        private readonly FlowLayoutPanel toolbarContent;
        private readonly Label scaleLabel;
        private readonly Button collapseButton;
        private readonly Button distanceButton;
        private readonly Button perimeterButton;
        private readonly Button areaButton;
        private readonly Button removeLastButton;
        private readonly Button clearButton;
        private readonly ComboBox scaleSelector;
        private readonly ComboBox unitSelector;
        private readonly ToolTip toolTip;

        private bool disposed;
        private bool isActive;
        private bool isCollapsed;
        private bool selectingScale;
        private bool scaleTextPending;
        private bool isCalibrating;
        private bool calibrationDialogOpen;
        private int activePageIndex = -1;
        private int draftPageIndex = -1;
        private PdfMeasurementKind activeKind =
            PdfMeasurementKind.Distance;
        private PdfMeasurementUnit activeUnit =
            PdfMeasurementUnit.Centimeter;
        private PdfMeasurementCalibration calibration;
        private Cursor cursorBeforeMeasurement;
        private string statusText = string.Empty;

        public PdfMeasurementController(
            PdfRenderer renderer,
            Func<bool> canActivate,
            Color accentColor,
            Color accentTextColor,
            Color surfaceColor)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException("renderer");
            }

            this.renderer = renderer;
            this.canActivate = canActivate;
            this.accentColor = accentColor;
            this.accentTextColor = accentTextColor;
            this.surfaceColor = surfaceColor;
            measurements = new List<PdfPageMeasurement>();
            readOnlyMeasurements = measurements.AsReadOnly();
            pageCalibrations =
                new Dictionary<int, PdfMeasurementCalibration>();
            measurementCalibrations =
                new Dictionary<
                    PdfPageMeasurement,
                    PdfMeasurementCalibration>();
            pageMarkers = new Dictionary<int, MeasurementPageMarker>();
            draftPoints = new List<PdfMeasurementPoint>();

            toolbar = new Panel
            {
                Width = 558,
                Height = 72,
                BackColor = surfaceColor,
                Visible = false,
                TabStop = false,
                AccessibleName = "Herramientas de medición"
            };
            toolbar.Paint += Toolbar_Paint;

            var titleLabel = new Label
            {
                AutoSize = false,
                Left = 10,
                Top = 7,
                Width = 52,
                Height = 20,
                Text = "MEDIR",
                ForeColor = accentTextColor,
                BackColor = Color.Transparent,
                Font = CreateArchitecturalFont(8f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            toolbar.Controls.Add(titleLabel);

            scaleLabel = new Label
            {
                AutoSize = false,
                Left = 64,
                Top = 7,
                Width = 438,
                Height = 20,
                Text = "Escala sin definir",
                ForeColor = Color.FromArgb(92, 92, 92),
                BackColor = Color.Transparent,
                Font = CreateArchitecturalFont(8f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            toolbar.Controls.Add(scaleLabel);

            collapseButton = CreateFlatButton(
                "\u2303",
                28,
                "Plegar herramientas de medición");
            collapseButton.Left = toolbar.Width - 34;
            collapseButton.Top = 3;
            collapseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            collapseButton.Click += CollapseButton_Click;
            toolbar.Controls.Add(collapseButton);

            toolbarContent = new FlowLayoutPanel
            {
                Left = 8,
                Top = 32,
                Width = toolbar.Width - 16,
                Height = 32,
                Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Left |
                    AnchorStyles.Right,
                WrapContents = false,
                AutoSize = false,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                TabStop = false
            };

            scaleSelector = new ComboBox
            {
                Width = 108,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDown,
                FlatStyle = FlatStyle.Flat,
                Font = CreateArchitecturalFont(8.5f, FontStyle.Regular),
                AccessibleName = "Escala del plano",
                AccessibleDescription =
                    "Escribe 75 o 1:75 y pulsa Enter, o elige una escala.",
                MaxLength = 24
            };
            scaleSelector.Items.AddRange(new object[]
            {
                "Escala 1:N...",
                "1:1",
                "1:20",
                "1:50",
                "1:100",
                "1:200",
                "Calibrar..."
            });
            scaleSelector.SelectedIndexChanged +=
                ScaleSelector_SelectedIndexChanged;
            scaleSelector.TextUpdate += ScaleSelector_TextUpdate;
            scaleSelector.KeyDown += ScaleSelector_KeyDown;
            scaleSelector.Leave += ScaleSelector_Leave;
            selectingScale = true;
            scaleSelector.SelectedIndex = 0;
            selectingScale = false;
            toolbarContent.Controls.Add(scaleSelector);

            distanceButton = CreateFlatButton(
                "\u2194",
                34,
                "Distancia: termina automáticamente al marcar dos puntos");
            distanceButton.Click += delegate
            {
                SelectKind(PdfMeasurementKind.Distance);
            };
            toolbarContent.Controls.Add(distanceButton);

            perimeterButton = CreateFlatButton(
                "\u2311",
                34,
                "Perímetro: Enter o doble clic para terminar");
            perimeterButton.Click += delegate
            {
                SelectKind(PdfMeasurementKind.Perimeter);
            };
            toolbarContent.Controls.Add(perimeterButton);

            areaButton = CreateFlatButton(
                "\u25B1",
                34,
                "Área: Enter o doble clic para cerrar");
            areaButton.Click += delegate
            {
                SelectKind(PdfMeasurementKind.Area);
            };
            toolbarContent.Controls.Add(areaButton);

            unitSelector = new ComboBox
            {
                Width = 63,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = CreateArchitecturalFont(8.5f, FontStyle.Regular),
                AccessibleName = "Unidad de medida"
            };
            unitSelector.Items.AddRange(new object[] { "mm", "cm", "m" });
            unitSelector.SelectedIndex = 1;
            unitSelector.SelectedIndexChanged +=
                UnitSelector_SelectedIndexChanged;
            toolbarContent.Controls.Add(unitSelector);

            removeLastButton = CreateFlatButton(
                "\u21B6",
                34,
                "Borrar la última medición de la página");
            removeLastButton.Click += delegate { RemoveLast(); };
            toolbarContent.Controls.Add(removeLastButton);

            clearButton = CreateFlatButton(
                "\u232B",
                34,
                "Borrar todas las mediciones de este documento");
            clearButton.Click += delegate { Clear(); };
            toolbarContent.Controls.Add(clearButton);

            toolbar.Controls.Add(toolbarContent);
            toolTip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 250,
                ReshowDelay = 80,
                ShowAlways = true
            };
            ApplyToolTips();
            UpdateKindButtons();

            renderer.Controls.Add(toolbar);
            PositionToolbar();
            toolbar.BringToFront();

            renderer.SizeChanged += Renderer_SizeChanged;
            renderer.SetCursor += Renderer_SetCursor;
            renderer.MouseMove += Renderer_MouseMove;
            renderer.Disposed += Renderer_Disposed;
            Application.AddMessageFilter(this);
        }

        public event EventHandler ActiveStateChanged;

        public event EventHandler StatusChanged;

        public bool IsActive
        {
            get { return isActive; }
        }

        public bool HasMeasurements
        {
            get { return measurements.Count > 0; }
        }

        internal ReadOnlyCollection<PdfPageMeasurement> Measurements
        {
            get { return readOnlyMeasurements; }
        }

        internal PdfMeasurementKind ActiveKind
        {
            get { return activeKind; }
        }

        internal PdfMeasurementUnit ActiveUnit
        {
            get { return activeUnit; }
        }

        internal PdfMeasurementCalibration Calibration
        {
            get { return calibration; }
        }

        internal string StatusText
        {
            get { return statusText; }
        }

        internal int DraftPointCount
        {
            get { return draftPoints.Count; }
        }

        internal int ActivePageIndex
        {
            get { return activePageIndex; }
        }

        internal bool IsCalibrating
        {
            get { return isCalibrating; }
        }

        internal Panel Toolbar
        {
            get { return toolbar; }
        }

        internal ComboBox ScaleSelector
        {
            get { return scaleSelector; }
        }

        internal bool HasPendingScaleText
        {
            get { return scaleTextPending; }
        }

        internal bool IsPrecisionCursorApplied
        {
            get
            {
                return !renderer.IsDisposed &&
                    Cursor.Current == Cursors.Cross;
            }
        }

        internal ComboBox UnitSelector
        {
            get { return unitSelector; }
        }

        public void Activate()
        {
            if (disposed || isActive)
            {
                return;
            }

            if (renderer.Document == null ||
                (canActivate != null && !canActivate()))
            {
                SetStatus("No se puede medir en este momento.");
                return;
            }

            cursorBeforeMeasurement = Cursor.Current;
            isActive = true;
            NotifyActivePage(renderer.Page);
            toolbar.Visible = true;
            toolbar.BringToFront();
            EnsurePageMarkersAttached();
            PositionToolbar();
            SetStatus(
                calibration == null
                    ? "Escribe 75 o 1:75, elige una escala o calibra."
                    : GetModeInstruction());
            OnActiveStateChanged();
            renderer.Focus();
            ApplyPrecisionCursor();
        }

        public void Deactivate()
        {
            if (disposed || !isActive)
            {
                return;
            }

            CancelDraft(false);
            isActive = false;
            if (scaleTextPending)
            {
                RestoreScaleSelection();
            }

            RestoreRendererCursor();
            toolbar.Visible = false;
            SetStatus(string.Empty);
            OnActiveStateChanged();
            if (!renderer.IsDisposed)
            {
                renderer.Invalidate();
            }
        }

        internal bool RemoveLast()
        {
            if (disposed)
            {
                return false;
            }

            if (draftPoints.Count > 0)
            {
                draftPoints.RemoveAt(draftPoints.Count - 1);
                if (draftPoints.Count == 0)
                {
                    var emptyDraftPage = draftPageIndex;
                    draftPageIndex = -1;
                    RemovePageMarkerIfUnused(emptyDraftPage);
                }

                renderer.Invalidate();
                SetStatus(
                    isCalibrating
                        ? "Calibración: marca dos puntos de una distancia conocida."
                        : GetDrawingInstruction());
                return true;
            }

            var pageIndex = GetCurrentPageIndex();
            for (var index = measurements.Count - 1; index >= 0; index--)
            {
                var measurement = measurements[index];
                if (measurement.PageIndex != pageIndex)
                {
                    continue;
                }

                measurements.RemoveAt(index);
                measurementCalibrations.Remove(measurement);
                RemovePageMarkerIfUnused(pageIndex);
                renderer.Invalidate();
                SetStatus(
                    "Última medición de la página borrada. " +
                    GetScaleInstruction());
                return true;
            }

            SetStatus("No hay mediciones en esta página para borrar.");
            return false;
        }

        public void Clear()
        {
            if (disposed)
            {
                return;
            }

            CancelDraft(false);
            measurements.Clear();
            measurementCalibrations.Clear();
            RemoveAllPageMarkers();
            SetStatus(
                isActive
                    ? "Mediciones borradas. " + GetScaleInstruction()
                    : string.Empty);
            if (!renderer.IsDisposed)
            {
                renderer.Invalidate();
            }
        }

        public void NotifyActivePage(int pageIndex)
        {
            if (disposed)
            {
                return;
            }

            var pageChanged = activePageIndex != pageIndex;
            activePageIndex = pageIndex;
            if (draftPoints.Count > 0 &&
                draftPageIndex >= 0 &&
                draftPageIndex != pageIndex)
            {
                CancelDraft(false);
                SetStatus(
                    isActive
                        ? "Trazado incompleto cancelado al cambiar de página."
                        : string.Empty);
            }

            if (pageChanged)
            {
                PdfMeasurementCalibration pageCalibration;
                calibration = pageCalibrations.TryGetValue(
                    pageIndex,
                    out pageCalibration)
                    ? pageCalibration
                    : null;
                RestoreScaleSelection();
                UpdateScaleLabel();
                if (isActive && draftPoints.Count == 0)
                {
                    SetStatus(GetScaleInstruction());
                }

                if (!renderer.IsDisposed)
                {
                    renderer.Invalidate();
                }
            }
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (disposed || !isActive)
            {
                return false;
            }

            if (message.Msg == WmKeyDown)
            {
                // Only the PDF canvas owns measurement shortcuts. Page
                // navigation fields, scale/unit selectors and the
                // calibration dialog must keep their normal Enter, Escape
                // and Backspace behaviour.
                if (calibrationDialogOpen ||
                    !renderer.IsHandleCreated ||
                    message.HWnd != renderer.Handle)
                {
                    return false;
                }

                return HandleKey((Keys)(int)message.WParam);
            }

            if (!renderer.IsHandleCreated ||
                message.HWnd != renderer.Handle)
            {
                return false;
            }

            if (message.Msg != WmLButtonDown &&
                message.Msg != WmLButtonDoubleClick)
            {
                return false;
            }

            if (scaleTextPending && !TryCommitScaleText())
            {
                scaleSelector.Focus();
                scaleSelector.SelectAll();
                return true;
            }

            if (!CanCapturePoint())
            {
                return false;
            }

            // Windows emits a normal button-down before the double-click
            // message. That first message has already added the final point.
            // Adding it again here would either distort a polygon or start a
            // phantom distance after the previous one auto-completed.
            if (message.Msg == WmLButtonDoubleClick)
            {
                if (!isCalibrating &&
                    activeKind != PdfMeasurementKind.Distance &&
                    draftPoints.Count > 0)
                {
                    FinishDraft();
                }

                return true;
            }

            var clientPoint = renderer.PointToClient(Cursor.Position);
            var pdfPoint = renderer.PointToPdf(clientPoint);
            if (!pdfPoint.IsValid)
            {
                return false;
            }

            var added = AddPdfPoint(pdfPoint);
            return added;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Deactivate();
            disposed = true;
            Application.RemoveMessageFilter(this);
            renderer.SizeChanged -= Renderer_SizeChanged;
            renderer.SetCursor -= Renderer_SetCursor;
            renderer.MouseMove -= Renderer_MouseMove;
            renderer.Disposed -= Renderer_Disposed;
            RemoveAllPageMarkers();

            if (!renderer.IsDisposed)
            {
                renderer.Controls.Remove(toolbar);
            }

            scaleSelector.SelectedIndexChanged -=
                ScaleSelector_SelectedIndexChanged;
            scaleSelector.TextUpdate -= ScaleSelector_TextUpdate;
            scaleSelector.KeyDown -= ScaleSelector_KeyDown;
            scaleSelector.Leave -= ScaleSelector_Leave;
            unitSelector.SelectedIndexChanged -=
                UnitSelector_SelectedIndexChanged;
            collapseButton.Click -= CollapseButton_Click;
            toolbar.Paint -= Toolbar_Paint;
            toolTip.Dispose();
            toolbar.Dispose();
        }

        internal void SelectKind(PdfMeasurementKind kind)
        {
            PdfMeasurementFormatter.ValidateKind(kind);
            if (activeKind == kind && !isCalibrating)
            {
                return;
            }

            CancelDraft(false);
            isCalibrating = false;
            activeKind = kind;
            UpdateKindButtons();
            SetStatus(GetScaleInstruction());
            renderer.Invalidate();
        }

        internal void SelectScale(double denominator)
        {
            if (disposed)
            {
                return;
            }

            calibration = PdfMeasurementCalibration.FromScale(denominator);
            scaleTextPending = false;
            scaleSelector.BackColor = SystemColors.Window;
            RememberCalibrationForActivePage(calibration);
            isCalibrating = false;
            CancelDraft(false);
            UpdateScaleLabel();
            SelectScaleItem(denominator);
            SetStatus(GetModeInstruction());
            renderer.Invalidate();
        }

        internal void SelectUnit(PdfMeasurementUnit unit)
        {
            PdfMeasurementFormatter.ValidateUnit(unit);
            activeUnit = unit;
            selectingScale = true;
            unitSelector.SelectedIndex = UnitToIndex(unit);
            selectingScale = false;
            renderer.Invalidate();
            SetStatus(
                calibration == null
                    ? "Escribe 75 o 1:75, elige una escala o calibra."
                    : GetModeInstruction());
        }

        internal bool AddPointForTesting(int pageIndex, PointF location)
        {
            return AddPdfPoint(new PdfPoint(pageIndex, location));
        }

        internal bool FinishDraftForTesting()
        {
            return FinishDraft();
        }

        private bool CanCapturePoint()
        {
            if (renderer.Document == null ||
                (canActivate != null && !canActivate()))
            {
                SetStatus("La medición se ha pausado.");
                return false;
            }

            if (!isCalibrating && calibration == null)
            {
                SetStatus(
                    "Escribe 75 o 1:75, elige una escala o calibra con dos puntos.");
                return false;
            }

            return true;
        }

        private bool AddPdfPoint(PdfPoint pdfPoint)
        {
            if (!pdfPoint.IsValid)
            {
                return false;
            }

            if (draftPoints.Count > 0 &&
                draftPageIndex != pdfPoint.Page)
            {
                CancelDraft(false);
                SetStatus(
                    "El trazado debe quedar dentro de una sola página.");
                return true;
            }

            var point = new PdfMeasurementPoint(
                pdfPoint.Location.X,
                pdfPoint.Location.Y);
            if (draftPoints.Count > 0 &&
                AreNear(draftPoints[draftPoints.Count - 1], point))
            {
                return true;
            }

            if (draftPoints.Count >= MaximumVertices)
            {
                SetStatus(
                    "Máximo de 100 vértices. Pulsa Enter para terminar.");
                return true;
            }

            if (draftPoints.Count == 0)
            {
                draftPageIndex = pdfPoint.Page;
            }

            draftPoints.Add(point);
            EnsurePageMarkerAttached(draftPageIndex);
            renderer.Invalidate();

            if (isCalibrating && draftPoints.Count == 2)
            {
                CompleteCalibration();
            }
            else if (!isCalibrating &&
                activeKind == PdfMeasurementKind.Distance &&
                draftPoints.Count == 2)
            {
                FinishDraft();
            }
            else
            {
                SetStatus(
                    isCalibrating
                        ? "Marca el segundo punto de la distancia conocida."
                        : GetDrawingInstruction());
            }

            return true;
        }

        private bool FinishDraft()
        {
            if (disposed || isCalibrating)
            {
                return false;
            }

            var requiredPoints =
                activeKind == PdfMeasurementKind.Distance ? 2 : 3;
            if (draftPoints.Count < requiredPoints)
            {
                SetStatus(
                    activeKind == PdfMeasurementKind.Distance
                        ? "Marca dos puntos para medir la distancia."
                        : "Marca al menos tres vértices.");
                return false;
            }

            if (measurements.Count >= MaximumMeasurements)
            {
                CancelDraft(false);
                SetStatus(
                    "Se ha alcanzado el máximo de 200 mediciones.");
                return false;
            }

            var measurement = new PdfPageMeasurement(
                draftPageIndex,
                activeKind,
                draftPoints);
            measurements.Add(measurement);
            if (calibration != null)
            {
                measurementCalibrations.Add(
                    measurement,
                    (PdfMeasurementCalibration)calibration.Clone());
            }
            var result = measurement.Format(calibration, activeUnit);
            var finishedPage = draftPageIndex;
            draftPoints.Clear();
            draftPageIndex = -1;
            EnsurePageMarkerAttached(finishedPage);
            renderer.Invalidate();
            SetStatus(result + ". " + GetModeInstruction());
            return true;
        }

        private void CompleteCalibration()
        {
            var first = draftPoints[0];
            var second = draftPoints[1];
            var deltaX = second.X - first.X;
            var deltaY = second.Y - first.Y;
            var pdfDistance = Math.Sqrt(
                deltaX * deltaX + deltaY * deltaY);
            if (pdfDistance <= 0.000001D)
            {
                CancelDraft(false);
                SetStatus("Los puntos de calibración deben estar separados.");
                return;
            }

            double knownValue;
            PdfMeasurementUnit knownUnit;
            using (var dialog = new CalibrationDialog(
                surfaceColor,
                accentColor,
                accentTextColor,
                activeUnit))
            {
                var owner = renderer.FindForm();
                DialogResult dialogResult;
                calibrationDialogOpen = true;
                try
                {
                    dialogResult = dialog.ShowDialog(owner);
                }
                finally
                {
                    calibrationDialogOpen = false;
                }

                if (dialogResult != DialogResult.OK)
                {
                    CancelDraft(false);
                    isCalibrating = false;
                    RestoreScaleSelection();
                    SetStatus(
                        calibration == null
                            ? "Calibración cancelada. Escribe o elige una escala."
                            : GetModeInstruction());
                    return;
                }

                knownValue = dialog.KnownValue;
                knownUnit = dialog.Unit;
            }

            calibration = PdfMeasurementCalibration.FromKnownDistance(
                pdfDistance,
                knownValue,
                knownUnit);
            RememberCalibrationForPage(draftPageIndex, calibration);
            activeUnit = knownUnit;
            selectingScale = true;
            scaleTextPending = false;
            scaleSelector.BackColor = SystemColors.Window;
            unitSelector.SelectedIndex = UnitToIndex(activeUnit);
            scaleSelector.SelectedIndex =
                scaleSelector.Items.Count - 1;
            selectingScale = false;
            draftPoints.Clear();
            draftPageIndex = -1;
            isCalibrating = false;
            UpdateScaleLabel();
            renderer.Invalidate();
            SetStatus("Calibración aplicada. " + GetModeInstruction());
        }

        private bool HandleKey(Keys key)
        {
            if (key == Keys.Escape)
            {
                if (draftPoints.Count > 0 || isCalibrating)
                {
                    CancelDraft(true);
                }
                else
                {
                    Deactivate();
                }

                return true;
            }

            if (key == Keys.Back && draftPoints.Count > 0)
            {
                draftPoints.RemoveAt(draftPoints.Count - 1);
                if (draftPoints.Count == 0)
                {
                    draftPageIndex = -1;
                }

                renderer.Invalidate();
                SetStatus(
                    isCalibrating
                        ? "Marca dos puntos de una distancia conocida."
                        : GetDrawingInstruction());
                return true;
            }

            if (key == Keys.Enter &&
                !isCalibrating &&
                activeKind != PdfMeasurementKind.Distance &&
                draftPoints.Count > 0)
            {
                FinishDraft();
                return true;
            }

            return false;
        }

        private void BeginCalibration()
        {
            CancelDraft(false);
            isCalibrating = true;
            UpdateScaleLabel();
            SetStatus(
                "Calibración: marca dos puntos de una distancia conocida.");
            renderer.Focus();
        }

        private void CancelDraft(bool announce)
        {
            draftPoints.Clear();
            draftPageIndex = -1;
            if (isCalibrating)
            {
                isCalibrating = false;
                RestoreScaleSelection();
                UpdateScaleLabel();
            }

            if (!renderer.IsDisposed)
            {
                renderer.Invalidate();
            }

            if (announce)
            {
                SetStatus(
                    calibration == null
                        ? "Trazado cancelado. Escribe o elige una escala."
                        : "Trazado cancelado. " + GetModeInstruction());
            }
        }

        private void ScaleSelector_SelectedIndexChanged(
            object sender,
            EventArgs e)
        {
            if (selectingScale)
            {
                return;
            }

            if (scaleSelector.SelectedIndex < 0)
            {
                return;
            }

            scaleTextPending = false;
            scaleSelector.BackColor = SystemColors.Window;
            switch (scaleSelector.SelectedIndex)
            {
                case 1:
                    SelectScale(1D);
                    break;

                case 2:
                    SelectScale(20D);
                    break;

                case 3:
                    SelectScale(50D);
                    break;

                case 4:
                    SelectScale(100D);
                    break;

                case 5:
                    SelectScale(200D);
                    break;

                case 6:
                    BeginCalibration();
                    break;

                default:
                    calibration = null;
                    pageCalibrations.Remove(GetCurrentPageIndex());
                    CancelDraft(false);
                    UpdateScaleLabel();
                    SetStatus(
                        "Escribe 75 o 1:75, elige una escala o calibra.");
                    renderer.Invalidate();
                    break;
            }
        }

        private void ScaleSelector_TextUpdate(object sender, EventArgs e)
        {
            if (disposed || selectingScale)
            {
                return;
            }

            scaleTextPending = true;
            scaleSelector.BackColor = SystemColors.Window;
            SetStatus(
                "Escala manual pendiente: pulsa Enter para aplicarla.");
        }

        private void ScaleSelector_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter &&
                (scaleTextPending || scaleSelector.SelectedIndex < 0))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (TryCommitScaleText())
                {
                    renderer.Focus();
                    ApplyPrecisionCursor();
                }
                else
                {
                    System.Media.SystemSounds.Beep.Play();
                    scaleSelector.SelectAll();
                }

                return;
            }

            if (e.KeyCode == Keys.Escape && scaleTextPending)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                RestoreScaleSelection();
                SetStatus(GetScaleInstruction());
            }
        }

        private void ScaleSelector_Leave(object sender, EventArgs e)
        {
            if (!disposed && !selectingScale && scaleTextPending)
            {
                TryCommitScaleText();
            }
        }

        private bool TryCommitScaleText()
        {
            double denominator;
            if (!TryParseScaleDenominator(
                    scaleSelector.Text,
                    out denominator))
            {
                SetInvalidScaleStatus();
                return false;
            }

            try
            {
                SelectScale(denominator);
                scaleTextPending = false;
                scaleSelector.BackColor = SystemColors.Window;
                return true;
            }
            catch (ArgumentException)
            {
                SetInvalidScaleStatus();
                return false;
            }
            catch (OverflowException)
            {
                SetInvalidScaleStatus();
                return false;
            }
        }

        private void SetInvalidScaleStatus()
        {
            scaleTextPending = true;
            scaleSelector.BackColor = Color.FromArgb(255, 238, 233);
            SetStatus(
                "Escala no válida. Escribe 75 o 1:75; admite coma o punto decimal.");
        }

        internal static bool TryParseScaleDenominator(
            string text,
            out double denominator)
        {
            denominator = 0D;
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return false;
            }

            var separatorIndex = text.IndexOf(':');
            if (separatorIndex >= 0)
            {
                if (separatorIndex != text.LastIndexOf(':') ||
                    !string.Equals(
                        text.Substring(0, separatorIndex).Trim(),
                        "1",
                        StringComparison.Ordinal))
                {
                    return false;
                }

                text = text.Substring(separatorIndex + 1).Trim();
                if (text.Length == 0)
                {
                    return false;
                }
            }

            if (text.IndexOf(',') >= 0 && text.IndexOf('.') >= 0)
            {
                return false;
            }

            text = text.Replace(',', '.');
            if (!double.TryParse(
                    text,
                    NumberStyles.AllowLeadingSign |
                        NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out denominator))
            {
                denominator = 0D;
                return false;
            }

            return denominator > 0D &&
                !double.IsNaN(denominator) &&
                !double.IsInfinity(denominator);
        }

        private void UnitSelector_SelectedIndexChanged(
            object sender,
            EventArgs e)
        {
            if (selectingScale || unitSelector.SelectedIndex < 0)
            {
                return;
            }

            activeUnit = IndexToUnit(unitSelector.SelectedIndex);
            renderer.Invalidate();
            SetStatus(GetScaleInstruction());
        }

        private void CollapseButton_Click(object sender, EventArgs e)
        {
            isCollapsed = !isCollapsed;
            toolbarContent.Visible = !isCollapsed;
            scaleLabel.Visible = !isCollapsed;
            toolbar.Width = isCollapsed ? 94 : 558;
            toolbar.Height = isCollapsed ? 32 : 72;
            collapseButton.Text = isCollapsed ? "\u2304" : "\u2303";
            toolTip.SetToolTip(
                collapseButton,
                isCollapsed
                    ? "Desplegar herramientas de medición"
                    : "Plegar herramientas de medición");
            PositionToolbar();
            toolbar.Invalidate();
        }

        private void Renderer_SizeChanged(object sender, EventArgs e)
        {
            PositionToolbar();
        }

        private void Renderer_SetCursor(
            object sender,
            SetCursorEventArgs e)
        {
            if (!disposed &&
                isActive &&
                e != null &&
                e.HitTest == HitTest.Client &&
                !toolbar.Bounds.Contains(e.Location))
            {
                e.Cursor = Cursors.Cross;
            }
        }

        private void Renderer_MouseMove(object sender, MouseEventArgs e)
        {
            if (!disposed &&
                isActive &&
                e != null &&
                !toolbar.Bounds.Contains(e.Location))
            {
                // PdfiumViewer forces a hand over links before raising its
                // normal SetCursor event. MouseMove runs afterwards and keeps
                // the exact centre hotspot of the native crosshair.
                Cursor.Current = Cursors.Cross;
            }
        }

        private void ApplyPrecisionCursor()
        {
            if (disposed || !isActive || renderer.IsDisposed)
            {
                return;
            }

            var location = renderer.PointToClient(Cursor.Position);
            if (renderer.ClientRectangle.Contains(location) &&
                !toolbar.Bounds.Contains(location))
            {
                Cursor.Current = Cursors.Cross;
            }
        }

        private void RestoreRendererCursor()
        {
            if (Cursor.Current == Cursors.Cross)
            {
                Cursor.Current = cursorBeforeMeasurement ?? Cursors.Default;
            }

            cursorBeforeMeasurement = null;
        }

        private void Renderer_Disposed(object sender, EventArgs e)
        {
            Dispose();
        }

        private void PositionToolbar()
        {
            if (renderer.IsDisposed || toolbar.IsDisposed)
            {
                return;
            }

            toolbar.Left = Math.Max(8, renderer.ClientSize.Width -
                toolbar.Width - 14);
            toolbar.Top = 12;
        }

        private void Toolbar_Paint(object sender, PaintEventArgs e)
        {
            using (var border = new Pen(
                Color.FromArgb(
                    128,
                    accentColor.R,
                    accentColor.G,
                    accentColor.B),
                1f))
            {
                e.Graphics.DrawRectangle(
                    border,
                    0,
                    0,
                    Math.Max(0, toolbar.ClientSize.Width - 1),
                    Math.Max(0, toolbar.ClientSize.Height - 1));
            }
        }

        private Button CreateFlatButton(
            string text,
            int width,
            string accessibleName)
        {
            var button = new Button
            {
                Width = width,
                Height = 28,
                Margin = new Padding(2, 0, 2, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = surfaceColor,
                ForeColor = accentTextColor,
                Text = text,
                Font = CreateArchitecturalFont(10f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                TabStop = false,
                AccessibleName = accessibleName
            };
            button.FlatAppearance.BorderColor =
                Color.FromArgb(205, 205, 205);
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor =
                Color.FromArgb(251, 236, 231);
            button.FlatAppearance.MouseDownBackColor =
                Color.FromArgb(244, 218, 209);
            return button;
        }

        private void UpdateKindButtons()
        {
            ApplyKindButtonState(
                distanceButton,
                activeKind == PdfMeasurementKind.Distance);
            ApplyKindButtonState(
                perimeterButton,
                activeKind == PdfMeasurementKind.Perimeter);
            ApplyKindButtonState(
                areaButton,
                activeKind == PdfMeasurementKind.Area);
        }

        private void ApplyKindButtonState(Button button, bool selected)
        {
            button.BackColor = selected
                ? Color.FromArgb(251, 236, 231)
                : surfaceColor;
            button.ForeColor = selected ? accentTextColor : Color.DimGray;
            button.FlatAppearance.BorderColor = selected
                ? accentColor
                : Color.FromArgb(205, 205, 205);
        }

        private void ApplyToolTips()
        {
            toolTip.SetToolTip(distanceButton, distanceButton.AccessibleName);
            toolTip.SetToolTip(perimeterButton, perimeterButton.AccessibleName);
            toolTip.SetToolTip(areaButton, areaButton.AccessibleName);
            toolTip.SetToolTip(
                removeLastButton,
                removeLastButton.AccessibleName);
            toolTip.SetToolTip(clearButton, clearButton.AccessibleName);
            toolTip.SetToolTip(
                scaleSelector,
                "Escribe 75 o 1:75 y pulsa Enter, o elige/calibra la escala");
            toolTip.SetToolTip(
                unitSelector,
                "Unidad utilizada en resultados y etiquetas");
            toolTip.SetToolTip(
                collapseButton,
                "Plegar herramientas de medición");
        }

        private void UpdateScaleLabel()
        {
            if (isCalibrating)
            {
                scaleLabel.Text = "Calibrando con 2 puntos";
                scaleLabel.ForeColor = accentTextColor;
            }
            else if (calibration == null)
            {
                scaleLabel.Text = "Escala sin definir";
                scaleLabel.ForeColor = Color.FromArgb(92, 92, 92);
            }
            else
            {
                scaleLabel.Text = calibration.Description;
                scaleLabel.ForeColor = accentTextColor;
            }
        }

        private void SelectScaleItem(double denominator)
        {
            var index = 0;
            if (Math.Abs(denominator - 1D) < 0.000001D)
            {
                index = 1;
            }
            else if (Math.Abs(denominator - 20D) < 0.000001D)
            {
                index = 2;
            }
            else if (Math.Abs(denominator - 50D) < 0.000001D)
            {
                index = 3;
            }
            else if (Math.Abs(denominator - 100D) < 0.000001D)
            {
                index = 4;
            }
            else if (Math.Abs(denominator - 200D) < 0.000001D)
            {
                index = 5;
            }

            selectingScale = true;
            scaleTextPending = false;
            scaleSelector.BackColor = SystemColors.Window;
            if (index > 0)
            {
                scaleSelector.SelectedIndex = index;
            }
            else
            {
                scaleSelector.SelectedIndex = -1;
                scaleSelector.Text =
                    "1:" +
                    PdfMeasurementFormatter.FormatScaleDenominator(
                        denominator);
            }
            selectingScale = false;
        }

        private void RestoreScaleSelection()
        {
            if (calibration == null)
            {
                selectingScale = true;
                scaleTextPending = false;
                scaleSelector.BackColor = SystemColors.Window;
                scaleSelector.SelectedIndex = 0;
                selectingScale = false;
                return;
            }

            var description = calibration.Description ?? string.Empty;
            if (description.StartsWith(
                "Escala 1:",
                StringComparison.OrdinalIgnoreCase))
            {
                var denominator =
                    calibration.RealMillimetersPerPdfPoint *
                    72D /
                    25.4D;
                SelectScaleItem(denominator);
                return;
            }

            if (description.StartsWith(
                "Calibración",
                StringComparison.OrdinalIgnoreCase))
            {
                selectingScale = true;
                scaleTextPending = false;
                scaleSelector.BackColor = SystemColors.Window;
                scaleSelector.SelectedIndex =
                    scaleSelector.Items.Count - 1;
                selectingScale = false;
            }
        }

        private string GetScaleInstruction()
        {
            return calibration == null
                ? "Escribe 75 o 1:75, elige una escala o calibra."
                : GetModeInstruction();
        }

        private string GetModeInstruction()
        {
            switch (activeKind)
            {
                case PdfMeasurementKind.Distance:
                    return "Distancia: marca dos puntos con el centro de la cruz.";

                case PdfMeasurementKind.Perimeter:
                    return "Perímetro: marca con la cruz y termina con Enter.";

                case PdfMeasurementKind.Area:
                    return "Área: marca con la cruz y termina con Enter.";

                default:
                    return string.Empty;
            }
        }

        private string GetDrawingInstruction()
        {
            if (draftPoints.Count == 0)
            {
                return GetModeInstruction();
            }

            if (activeKind == PdfMeasurementKind.Distance)
            {
                return "Marca el segundo punto.";
            }

            return draftPoints.Count < 3
                ? "Marca al menos " + (3 - draftPoints.Count) +
                    " vértice(s) más."
                : "Enter o doble clic para terminar; Retroceso deshace.";
        }

        private void SetStatus(string value)
        {
            value = value ?? string.Empty;
            if (string.Equals(statusText, value, StringComparison.Ordinal))
            {
                return;
            }

            statusText = value;
            var handler = StatusChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void OnActiveStateChanged()
        {
            var handler = ActiveStateChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void EnsurePageMarkersAttached()
        {
            foreach (var page in GetMeasuredPages())
            {
                EnsurePageMarkerAttached(page);
            }

            if (draftPageIndex >= 0)
            {
                EnsurePageMarkerAttached(draftPageIndex);
            }
        }

        private IEnumerable<int> GetMeasuredPages()
        {
            var seen = new HashSet<int>();
            foreach (var measurement in measurements)
            {
                if (seen.Add(measurement.PageIndex))
                {
                    yield return measurement.PageIndex;
                }
            }
        }

        private void EnsurePageMarkerAttached(int pageIndex)
        {
            if (pageIndex < 0 || renderer.IsDisposed)
            {
                return;
            }

            MeasurementPageMarker marker;
            if (!pageMarkers.TryGetValue(pageIndex, out marker))
            {
                marker = new MeasurementPageMarker(this, pageIndex);
                pageMarkers.Add(pageIndex, marker);
            }

            if (!renderer.Markers.Contains(marker))
            {
                renderer.Markers.Add(marker);
            }
        }

        private void RemoveAllPageMarkers()
        {
            if (!renderer.IsDisposed)
            {
                foreach (var marker in pageMarkers.Values)
                {
                    if (renderer.Markers.Contains(marker))
                    {
                        renderer.Markers.Remove(marker);
                    }
                }
            }

            pageMarkers.Clear();
        }

        private void RemovePageMarkerIfUnused(int pageIndex)
        {
            if (pageIndex < 0 || draftPageIndex == pageIndex)
            {
                return;
            }

            foreach (var measurement in measurements)
            {
                if (measurement.PageIndex == pageIndex)
                {
                    return;
                }
            }

            MeasurementPageMarker marker;
            if (!pageMarkers.TryGetValue(pageIndex, out marker))
            {
                return;
            }

            if (!renderer.IsDisposed && renderer.Markers.Contains(marker))
            {
                renderer.Markers.Remove(marker);
            }

            pageMarkers.Remove(pageIndex);
        }

        private void DrawPage(
            PdfRenderer targetRenderer,
            Graphics graphics,
            int pageIndex)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var measurement in measurements)
            {
                if (measurement.PageIndex == pageIndex)
                {
                    DrawMeasurement(
                        targetRenderer,
                        graphics,
                        measurement,
                        false);
                }
            }

            if (draftPageIndex == pageIndex && draftPoints.Count > 0)
            {
                DrawGeometry(
                    targetRenderer,
                    graphics,
                    pageIndex,
                    draftPoints,
                    activeKind,
                    false,
                    true);
            }
        }

        private void DrawMeasurement(
            PdfRenderer targetRenderer,
            Graphics graphics,
            PdfPageMeasurement measurement,
            bool draft)
        {
            DrawGeometry(
                targetRenderer,
                graphics,
                measurement.PageIndex,
                measurement.Points,
                measurement.Kind,
                measurement.Kind != PdfMeasurementKind.Distance,
                draft);

            PdfMeasurementCalibration measurementCalibration;
            if (!measurementCalibrations.TryGetValue(
                    measurement,
                    out measurementCalibration))
            {
                measurementCalibration = calibration;
            }

            if (measurementCalibration != null)
            {
                DrawLabel(
                    targetRenderer,
                    graphics,
                    measurement,
                    measurement.Format(
                        measurementCalibration,
                        activeUnit));
            }
        }

        private int GetCurrentPageIndex()
        {
            if (draftPageIndex >= 0)
            {
                return draftPageIndex;
            }

            if (activePageIndex >= 0)
            {
                return activePageIndex;
            }

            return renderer.Document == null ? -1 : renderer.Page;
        }

        private void RememberCalibrationForActivePage(
            PdfMeasurementCalibration value)
        {
            RememberCalibrationForPage(GetCurrentPageIndex(), value);
        }

        private void RememberCalibrationForPage(
            int pageIndex,
            PdfMeasurementCalibration value)
        {
            if (pageIndex < 0 || value == null)
            {
                return;
            }

            pageCalibrations[pageIndex] =
                (PdfMeasurementCalibration)value.Clone();
        }

        private void DrawGeometry(
            PdfRenderer targetRenderer,
            Graphics graphics,
            int pageIndex,
            IList<PdfMeasurementPoint> points,
            PdfMeasurementKind kind,
            bool close,
            bool draft)
        {
            if (points.Count == 0)
            {
                return;
            }

            var clientPoints = new Point[points.Count];
            for (var index = 0; index < points.Count; index++)
            {
                clientPoints[index] = targetRenderer.PointFromPdf(
                    new PdfPoint(
                        pageIndex,
                        new PointF(
                            (float)points[index].X,
                            (float)points[index].Y)));
            }

            if (!draft &&
                kind == PdfMeasurementKind.Area &&
                clientPoints.Length >= 3)
            {
                using (var fill = new SolidBrush(
                    Color.FromArgb(
                        26,
                        accentColor.R,
                        accentColor.G,
                        accentColor.B)))
                {
                    graphics.FillPolygon(fill, clientPoints);
                }
            }

            using (var pen = new Pen(
                Color.FromArgb(
                    draft ? 185 : 230,
                    accentColor.R,
                    accentColor.G,
                    accentColor.B),
                draft ? 1.4f : 2f))
            {
                if (draft)
                {
                    pen.DashStyle = DashStyle.Dash;
                }

                if (clientPoints.Length >= 2)
                {
                    graphics.DrawLines(pen, clientPoints);
                    if (close && clientPoints.Length >= 3)
                    {
                        graphics.DrawLine(
                            pen,
                            clientPoints[clientPoints.Length - 1],
                            clientPoints[0]);
                    }
                }

                foreach (var point in clientPoints)
                {
                    var radius = draft ? 3 : 4;
                    using (var pointFill = new SolidBrush(surfaceColor))
                    {
                        graphics.FillEllipse(
                            pointFill,
                            point.X - radius,
                            point.Y - radius,
                            radius * 2,
                            radius * 2);
                    }

                    graphics.DrawEllipse(
                        pen,
                        point.X - radius,
                        point.Y - radius,
                        radius * 2,
                        radius * 2);
                }
            }
        }

        private void DrawLabel(
            PdfRenderer targetRenderer,
            Graphics graphics,
            PdfPageMeasurement measurement,
            string text)
        {
            if (measurement.Points.Count == 0 ||
                string.IsNullOrEmpty(text))
            {
                return;
            }

            double x = 0D;
            double y = 0D;
            foreach (var point in measurement.Points)
            {
                x += point.X;
                y += point.Y;
            }

            x /= measurement.Points.Count;
            y /= measurement.Points.Count;
            var anchor = targetRenderer.PointFromPdf(
                new PdfPoint(
                    measurement.PageIndex,
                    new PointF((float)x, (float)y)));

            using (var font = CreateArchitecturalFont(
                8.5f,
                FontStyle.Bold))
            {
                var textSize = graphics.MeasureString(text, font);
                var labelBounds = new RectangleF(
                    anchor.X + 7f,
                    anchor.Y - textSize.Height - 7f,
                    textSize.Width + 10f,
                    textSize.Height + 5f);
                using (var background = new SolidBrush(
                    Color.FromArgb(
                        232,
                        surfaceColor.R,
                        surfaceColor.G,
                        surfaceColor.B)))
                using (var outline = new Pen(
                    Color.FromArgb(
                        180,
                        accentColor.R,
                        accentColor.G,
                        accentColor.B)))
                using (var textBrush = new SolidBrush(accentTextColor))
                {
                    graphics.FillRectangle(background, labelBounds);
                    graphics.DrawRectangle(
                        outline,
                        labelBounds.X,
                        labelBounds.Y,
                        labelBounds.Width,
                        labelBounds.Height);
                    graphics.DrawString(
                        text,
                        font,
                        textBrush,
                        labelBounds.X + 5f,
                        labelBounds.Y + 2f);
                }
            }
        }

        private static bool AreNear(
            PdfMeasurementPoint left,
            PdfMeasurementPoint right)
        {
            var dx = left.X - right.X;
            var dy = left.Y - right.Y;
            return (dx * dx + dy * dy) <=
                DuplicatePointTolerance * DuplicatePointTolerance;
        }

        private static PdfMeasurementUnit IndexToUnit(int index)
        {
            switch (index)
            {
                case 0:
                    return PdfMeasurementUnit.Millimeter;

                case 2:
                    return PdfMeasurementUnit.Meter;

                default:
                    return PdfMeasurementUnit.Centimeter;
            }
        }

        private static int UnitToIndex(PdfMeasurementUnit unit)
        {
            switch (unit)
            {
                case PdfMeasurementUnit.Millimeter:
                    return 0;

                case PdfMeasurementUnit.Meter:
                    return 2;

                default:
                    return 1;
            }
        }

        private static Font CreateArchitecturalFont(
            float size,
            FontStyle style)
        {
            var families = new[]
            {
                "Bahnschrift Light SemiCondensed",
                "Bahnschrift Light",
                "Segoe UI Semilight",
                "Segoe UI"
            };
            foreach (var family in families)
            {
                try
                {
                    return new Font(
                        family,
                        size,
                        style,
                        GraphicsUnit.Point);
                }
                catch
                {
                }
            }

            return new Font(
                SystemFonts.MessageBoxFont.FontFamily,
                size,
                style,
                GraphicsUnit.Point);
        }

        private sealed class MeasurementPageMarker : IPdfMarker
        {
            private readonly PdfMeasurementController owner;
            private readonly int page;

            public MeasurementPageMarker(
                PdfMeasurementController owner,
                int page)
            {
                this.owner = owner;
                this.page = page;
            }

            public int Page
            {
                get { return page; }
            }

            public void Draw(PdfRenderer pdfRenderer, Graphics graphics)
            {
                if (!owner.disposed)
                {
                    owner.DrawPage(pdfRenderer, graphics, page);
                }
            }
        }

        private sealed class CalibrationDialog : Form
        {
            private readonly NumericUpDown valueInput;
            private readonly ComboBox unitInput;

            public CalibrationDialog(
                Color surfaceColor,
                Color accentColor,
                Color accentTextColor,
                PdfMeasurementUnit initialUnit)
            {
                Text = "Calibrar escala";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false;
                MinimizeBox = false;
                MaximizeBox = false;
                ClientSize = new Size(326, 142);
                BackColor = surfaceColor;
                ForeColor = accentTextColor;
                Font = CreateArchitecturalFont(
                    9f,
                    FontStyle.Regular);

                var prompt = new Label
                {
                    Left = 16,
                    Top = 16,
                    Width = 294,
                    Height = 34,
                    Text = "Distancia real entre los dos puntos:",
                    TextAlign = ContentAlignment.MiddleLeft
                };
                Controls.Add(prompt);

                valueInput = new NumericUpDown
                {
                    Left = 16,
                    Top = 56,
                    Width = 190,
                    DecimalPlaces = 3,
                    Minimum = 0.001M,
                    Maximum = 1000000000M,
                    Value = 1M,
                    ThousandsSeparator = true
                };
                Controls.Add(valueInput);

                unitInput = new ComboBox
                {
                    Left = 214,
                    Top = 56,
                    Width = 74,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    FlatStyle = FlatStyle.Flat
                };
                unitInput.Items.AddRange(
                    new object[] { "mm", "cm", "m" });
                unitInput.SelectedIndex = UnitToIndex(initialUnit);
                Controls.Add(unitInput);

                var accept = new Button
                {
                    Left = 142,
                    Top = 99,
                    Width = 80,
                    Height = 28,
                    Text = "Aplicar",
                    DialogResult = DialogResult.OK,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = accentColor,
                    ForeColor = Color.White
                };
                accept.FlatAppearance.BorderSize = 0;
                Controls.Add(accept);

                var cancel = new Button
                {
                    Left = 230,
                    Top = 99,
                    Width = 80,
                    Height = 28,
                    Text = "Cancelar",
                    DialogResult = DialogResult.Cancel,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = surfaceColor,
                    ForeColor = accentTextColor
                };
                cancel.FlatAppearance.BorderColor =
                    Color.FromArgb(190, 190, 190);
                Controls.Add(cancel);

                AcceptButton = accept;
                CancelButton = cancel;
            }

            public double KnownValue
            {
                get { return (double)valueInput.Value; }
            }

            public PdfMeasurementUnit Unit
            {
                get { return IndexToUnit(unitInput.SelectedIndex); }
            }
        }
    }
}
