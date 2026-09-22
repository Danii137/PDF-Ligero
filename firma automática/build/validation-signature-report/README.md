# Ver y validar las firmas de un PDF

Firma de verdad un PDF con un certificado generado en la propia prueba, y luego
manipula el archivo. Lo que importa no es que lea el nombre del firmante, sino
que **cante cuando el documento ha cambiado después de firmarse**.

## Qué comprueba

- **Sin firmar**: no se inventa firmas.
- **Firmado**: lee quién firma y la fecha, y dice que cubre el documento entero.
  Y sale **con avisos**, no como «firma válida» a secas, porque el certificado
  se lo ha hecho la propia prueba y no lo avala ninguna autoridad. Que un PDF
  firmado con un certificado casero saliera «firma válida» sería el fallo
  peligroso de verdad.
- **Manipulado**: se cambia una letra del texto dentro de lo que cubre la firma
  y tiene que salir «Firma no válida».
- **Con añadido posterior**: una revisión incremental no rompe la firma, pero
  deja de cubrir el documento entero y hay que decirlo.

El fixture se escribe sin comprimir a propósito, para poder cambiar una letra
con el archivo en la mano.

## Lo que no cubre

No se consultan listas de revocación ni OCSP, así que no se puede saber si un
certificado fue anulado. El servicio lo dice como aviso en cada firma, y esta
prueba no lo comprueba porque no hay nada que comprobar.

## Ejecución

```powershell
Set-Location -LiteralPath '...\firma automática\build\validation-signature-report'
.\compile-and-run.ps1
```

## Resultado de referencia

```text
Sin firmar: Este PDF no está firmado.
Firmado: "Estudio de prueba AGOIN" · Firma válida, con avisos · 2 avisos
Manipulado: Firma no válida
Con añadido posterior: Firma válida, con avisos, cubre todo = no
PASS: las firmas se leen y se validan.
```
