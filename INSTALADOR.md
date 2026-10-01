# Prompt para el instalador de ARBA — añadir el plugin Losas

Añade al instalador de los plugins ARBA para Revit 2027 el nuevo add-in **Losas**
(armado de losas aligeradas y macizas). Es el hermano de **Columnas** (Acero-columnas) y
de **Muros** (Acero-automatico): se instala exactamente igual y aparece en el mismo sitio
de la cinta. No crees ningún botón ni pestaña desde el instalador: la cinta la crea el
propio add-in al arrancar Revit.

## Origen

- Repositorio: https://github.com/Andy-rba30/Acero-losas, rama `claude/brave-allen-3gowqo`
  (o `main` cuando se fusione).
- Proyecto: `SlabRebar.csproj` (.NET 10, `net10.0-windows`, x64). Compilar con
  `dotnet build -c Release`. La salida está en `bin\Release\net10.0-windows\`.
- No tiene dependencias aparte de las DLL de Revit (los paquetes `Nice3point.Revit.Api.*`
  son solo de compilación, no se copian). No hace falta copiar nada de `bin` salvo
  `SlabRebar.dll` (y `SlabRebar.pdb` si quieres depurar).

## Archivos a instalar (por usuario, `%AppData%\Autodesk\Revit\Addins\2027\`)

```
%AppData%\Autodesk\Revit\Addins\2027\SlabRebar.addin          <- del repo (raiz)
%AppData%\Autodesk\Revit\Addins\2027\SlabRebar\SlabRebar.dll  <- de bin\Release\net10.0-windows\
%AppData%\Autodesk\Revit\Addins\2027\SlabRebar\config.json    <- del repo (raiz); NO sobrescribir si ya existe
                                                                  (guarda los valores por defecto del usuario)
```

El manifiesto `SlabRebar.addin` ya trae las rutas relativas `SlabRebar\SlabRebar.dll`, los
dos registros (Application `SlabRebar.RibbonApp` con ClientId
`c3e7a9d2-5b14-4f8e-9a6b-7d2e0f1c4a58` y Command `SlabRebar.ArmarLosaCommand` con ClientId
`e81f2b6c-4a9d-47c3-b5e0-6f3d8a2c9b17`), `VendorId` LOCAL. No lo modifiques. Si el
instalador usa una carpeta común para todos los add-ins ARBA en lugar de una por plugin,
ajusta solo la etiqueta `<Assembly>` del manifiesto para que apunte a la ruta real de la
DLL. `config.json` tiene que quedar siempre en la misma carpeta que `SlabRebar.dll`
(el add-in lo busca junto a su ensamblado).

## Dónde aparece en Revit

Pestaña **ARBA** > panel **Acero** > desplegable **Acero** > botón **Losas**, junto a
**Columnas** y **Muros**. Los tres add-ins llevan la misma clase `ArbaRibbon`: cada uno
crea la pestaña ARBA y los paneles IA / Acero / Encofrado si no existen (en ese orden) y
añade su botón al desplegable "Acero" del panel "Acero", así que da igual cuál cargue
primero y basta con instalar los archivos. Si se desinstala Losas, solo hay que borrar
`SlabRebar.addin` y la carpeta `SlabRebar\`; el desplegable sigue con los demás botones.

## Comprobación tras instalar

1. Abrir Revit 2027.2 y aceptar la carga del add-in (si pide confirmación por el VendorId).
2. En la pestaña ARBA, panel Acero, desplegable Acero debe estar **Losas** con su icono
   (sección de losa con ladrillos). También aparece en Complementos > Herramientas
   externas > "Armar losa".
3. Seleccionar un suelo estructural de hormigón y pulsar Losas: se abre la ventana
   "Armar losas" con el tema oscuro de Revit.

## Desinstalación

Borrar `%AppData%\Autodesk\Revit\Addins\2027\SlabRebar.addin` y la carpeta
`%AppData%\Autodesk\Revit\Addins\2027\SlabRebar\`.
