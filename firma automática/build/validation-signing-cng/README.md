# Firmar con la clave privada tal como la entrega Windows

## El problema

`SigningFlowController` obtenía la clave con `certificate.PrivateKey`, que solo
sirve para claves guardadas en un proveedor CSP clásico. Con una clave en CNG
esa propiedad **no devuelve null: lanza** `CryptographicException: Se ha
especificado un tipo de proveedor no válido`, así que ni siquiera se llegaba a
los caminos de reserva que había debajo.

En el registro del programa hay un caso real, del 17 de septiembre de 2026, con
ese mensaje exacto.

## Qué comprueba

- **Con qué clave trabaja Windows**: para un certificado generado aquí, Windows
  devuelve `RSACng`.
- **La firma del camino nuevo es válida**: se firma un PDF con
  `PdfModernRsaSignature` y se valida con `PdfSignatureInspectionService`. Si el
  resumen estuviera mal, saldría «Firma no válida».
- **No se resume dos veces**: la firma sale **byte a byte igual** que la de
  `PrivateKeySignature`, la implementación de referencia de iText. Ése es el
  error típico al escribir un `IExternalSignature`: hacer el hash antes de
  firmar, cuando iText entrega el mensaje en claro.

## Lo que esta prueba NO hace

**No reproduce el lanzamiento de producción.** Con una clave exportable como la
que se fabrica aquí, .NET sabe construir un `RSACryptoServiceProvider` a partir
de la clave CNG y el camino clásico funciona — la propia prueba lo imprime. El
fallo aparece con claves **no exportables**: certificados de la FNMT instalados
con protección fuerte, DNIe y tokens hardware, que no se pueden fabricar sin ese
hardware.

Lo que evita una regresión es el orden: el camino clásico sigue siendo el
primero y no se ha tocado. El nuevo solo entra cuando el viejo lanza.

## Ejecución

```powershell
Set-Location -LiteralPath '...\firma automática\build\validation-signing-cng'
.\compile-and-run.ps1
```

## Resultado de referencia

```text
Clave privada de Windows: RSACng
Camino clasico (certificate.PrivateKey): funciona con esta clave exportable
Firma con clave moderna: Firma válida, con avisos · Estudio CNG de prueba
Byte a byte igual que la firma de referencia de iText.
PASS: la firma con clave moderna es válida.
```
