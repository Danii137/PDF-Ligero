# Validación integrada de edición de texto

Este smoke carga `build/output/PDFLigero.exe` por reflexión y ejercita el
visor real sin abrir cuadros de diálogo ni modificar archivos de producción.

Ejecutar desde PowerShell:

```powershell
.\run-smoke.ps1
```

Cada ejecución crea una carpeta `run-*` con el informe, los dos PDF originales
de prueba y capturas de la selección y de la revisión activa.
