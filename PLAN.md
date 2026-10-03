# Plan de trabajo — add-in ARBA Losas (Revit 2027.2)

Este archivo es el plan vivo del proyecto y su estado. Se actualiza en cada commit
para que, si se corta la sesion, se pueda retomar exactamente donde se quedo.

## Objetivo

Add-in de Revit 2027 en C# (.NET 10, WPF en codigo, sin XAML) que arme **losas**:

- **Losas aligeradas** (viguetas + ladrillo de techo + losa superior): barra inferior
  de cada vigueta, bastones superiores en los apoyos (o barra superior corrida) y
  **acero de temperatura** en la losa superior, perpendicular a las viguetas.
- **Losas macizas**: malla inferior (direccion principal + direccion secundaria, que
  es el acero de temperatura/reparticion en losas en una direccion) y, opcional, malla
  superior corrida o bastones en los apoyos.

Mismo aspecto visual y misma arquitectura que
[Acero-columnas](https://github.com/Andy-rba30/Acero-columnas): tema oscuro de
Revit (`RevitTheme`), ventana previa con esquemas (planta + seccion), lista de
elementos con ajustes propios por elemento, `config.json` con valores por defecto,
boton en la pestana **ARBA** > panel **Acero** > desplegable **Acero** > **Losas**,
red de seguridad que deshace el elemento entero si una barra queda fuera del
hormigon. Desde la integracion de [ARBA-comun](https://github.com/Andy-rba30/ARBA-comun)
(contrato 1.0.0, submodulo `external/ARBA-comun`) la cinta, el tema, la particion, los
parametros compartidos y las reglas de borrar/rearmar y migrar son el codigo comun de todos
los add-ins ARBA.

## Arquitectura (archivos)

| Archivo | Que hace | Estado |
|---------|----------|--------|
| `SlabRebar.csproj`, `SlabRebar.addin`, `config.json`, `.gitignore` | Proyecto .NET 10 (net10.0-windows), manifiesto, configuracion | hecho |
| `external/ARBA-comun/` | Submodulo (etiqueta `v1.0.0`) con el codigo comun ARBA compilado como fuente: `ArbaContract`, `ArbaPartition`, `PartitionName`, `ArbaSharedParams`, `ArbaOrigin`, `ArbaMigration`, `ArbaRibbon`, `RevitTheme`, `NameMatch`. No se edita desde este repo | hecho |
| `RibbonApp.cs` | Boton **Losas** con icono propio en la cinta comun `ArbaRibbon` (`Ensure` + `AddAcero`) | hecho |
| `AppConfig.cs` | Configuracion (`config.json`): recubrimientos, direccion, aligerada, maciza, temperatura, plantilla de particion del contrato (`{categoria} - {prefijo}-{marca}`) | hecho |
| ~~`RevitTheme.cs`~~, ~~`PartitionName.cs`~~ | Borrados: ahora son los de `external/ARBA-comun/src` | hecho |
| `Geometry2D.cs` | Geometria pura: `Pt`, poligonos con huecos, recorte de una recta contra el contorno (scan-line), eje del borde mas largo, caja envolvente | hecho |
| `SlabOutline.cs` | Lectura del solido de Revit: cara superior plana, contorno exterior y huecos, espesor, ejes locales, vigas de apoyo | hecho |
| `SlabPlan.cs` | Armado puro: viguetas, barras por capa y direccion, bastones por apoyos, temperatura, agrupacion en arrays | hecho |
| `HostAnalysis.cs` | Resultado por losa (contorno o motivo de rechazo) + elecciones por losa (tipo, direccion); particion con `ArbaPartition.BuildFor` | hecho |
| `RebarGenerator.cs` | Crea los `Rebar` con las dos redes de seguridad; escribe Particion, `ARBA - Origen`, `ARBA - Codigo` y `Metrado - Elemento` con el comun | hecho |
| `RebarOptionsWindow.cs` | Ventana WPF en codigo, mismo aspecto que columnas; aviso de plantilla fuera del contrato y version del contrato en el pie | hecho |
| `PlanPreview.cs` | Esquema en planta (zoom/arrastrar/doble clic) | hecho |
| `SectionPreview.cs` | Esquema de la seccion transversal (ladrillos, viguetas, barras, temperatura) | hecho |
| `ArmarLosaCommand.cs` | Comando externo: seleccion de losas, analisis, ventana, parametros compartidos (`ArbaSharedParams.Ensure`), borrar y rearmar / conservar / migrar sin rearmar, transaccion, informe | hecho |
| `README.md` | Documentacion de uso al estilo del add-in de columnas | hecho |
| `Tests/` | Programa de consola que prueba las clases puras (`Geometry2D`, `SlabPlan`, `AppConfig` y las puras del comun): `cd Tests && dotnet run` | hecho (172 comprobaciones) |

## Decisiones de diseño

1. **Geometria real del elemento**: se lee el solido del suelo (`Floor`), se exige una
   unica cara superior plana horizontal; el contorno exterior y los huecos salen de los
   `CurveLoop` de esa cara (bordes rectos; los curvos se teselan). Espesor = cara
   superior menos cara inferior (o el espesor del tipo si la losa esta cortada).
2. **Ejes locales**: `u` = direccion de las barras principales (viguetas), `v` =
   perpendicular. La direccion se elige: lado corto (por defecto, las viguetas van en
   la luz menor), lado largo, X o Y del proyecto, o un angulo. Cambiable por losa.
3. **Recorte scan-line**: cada linea de barra (v = cte) se corta contra el contorno con
   huecos; cada intervalo interior es una barra. Vale para cualquier poligono (L, con
   huecos, irregular). Las lineas consecutivas con los mismos intervalos se agrupan en
   un unico conjunto de Revit (array), como en columnas.
4. **Prolongacion en los extremos**: las barras pueden sobresalir del contorno
   exterior (anclaje en la viga) una longitud dada; en los huecos siempre paran al
   recubrimiento. La parte que sobresale es la unica que no se comprueba.
5. **Apoyos**: opcionalmente se detectan las vigas (`OST_StructuralFraming`) que cruzan
   la losa casi perpendiculares a `u`; sirven para colocar los bastones (L/4 interiores,
   L/5 extremos, configurables, o longitud fija) y su hormigon cuenta como valido en la
   comprobacion.
6. **Tipo de losa**: por defecto segun el nombre del tipo (contiene "aliger" ->
   aligerada), con eleccion por losa en la lista.
7. **Acero de temperatura (aligerada)**: barras a lo largo de `v` en la losa superior,
   por debajo de los bastones (o a la profundidad que se indique), separacion dada.
8. **Red de seguridad**: igual que columnas. (1) antes de crear, eje + fibras a medio
   diametro dentro del solido en todas las posiciones del array (solo el tramo dentro
   del contorno); (2) tras regenerar, geometria real de cada barra. Cualquier fallo
   deshace la subtransaccion del elemento.
9. **Contrato ARBA-comun 1.0.0** (`external/ARBA-comun/CONTRATO.md`): particion
   `LOSAS - LOS-{marca}` (categoria del anfitrion + prefijo del add-in; la capa no entra,
   va en `ARBA - Codigo`), parametros compartidos de ejemplar con GUID fijo
   (`ARBA - Origen` = LOSAS, `ARBA - Codigo` = capa, `Metrado - Elemento` = LOSAS) creados
   al abrir la transaccion, "borrar y rearmar / conservar" cuando la losa ya tiene conjuntos
   propios (`ArbaOrigin.Find`/`Delete`) y migracion de barras antiguas `LOSA-…`
   (`ArbaMigration.HasLegacy`/`MigrateHost`) antes de rearmar o sin rearmar. El codigo comun
   se compila como fuente dentro de `SlabRebar.dll` (nunca DLL compartida) y no se modifica
   desde este repo.

## Progreso

- [x] Analisis del repo de referencia (Acero-columnas) y de su estilo visual.
- [x] Plan guardado.
- [x] Proyecto, manifiesto, config, tema, cinta.
- [x] Geometria pura y plan de armado + pruebas de consola (115 OK).
- [x] Lectura del solido de Revit y apoyos.
- [x] Generador con redes de seguridad.
- [x] Ventana y esquemas.
- [x] Comando e informe.
- [x] README.
- [x] Compilacion (dotnet build con EnableWindowsTargeting): 0 errores, 0 avisos.
- [x] Revision del codigo: union de tramos interiores por parametro al comprobar contra losa + vigas,
      vigas detectadas siempre y usadas segun la casilla, limitaciones documentadas.
- [x] Prueba en Revit 2027.2 del armado (sesiones anteriores).
- [x] Integracion de ARBA-comun v1.0.0: submodulo + `Arba.Comun.props`, cinta comun, borrado de
      `RevitTheme.cs` y `PartitionName.cs`, plantilla del contrato, origen/codigo/elemento,
      borrar y rearmar, migracion, aviso de plantilla y version en la ventana, tests (172 OK),
      `dotnet build -c Release` 0 errores 0 avisos.
- [ ] Lista de verificacion del contrato en Revit (usuario): una sola pestana ARBA con "Losas"
      en el desplegable Acero; aligerada marca L2 -> Particion `LOSAS - LOS-L2` en todos los
      conjuntos, `ARBA - Origen = LOSAS`, `ARBA - Codigo` inferior/baston/temperatura,
      `Metrado - Elemento = LOSAS` (maciza: inferior, inferior-sec, superior, superior-sec);
      rearmar pregunta borrar/conservar y con "borrar" no quedan duplicados; modelo con barras
      `LOSA-L1` ofrece migrar y queda `LOSAS - LOS-L1` con origen; "Metrado acero - Losas" del
      plugin de metrados agrupa por `LOSAS - LOS-…`.

## Como retomar

1. `git pull` de `main` y `git submodule update --init` (el codigo comun esta en
   `external/ARBA-comun`, etiqueta `v1.0.0`; para subir de version,
   `git -C external/ARBA-comun checkout vX.Y.Z` y commit del puntero).
2. `dotnet build -c Debug` en Windows con Revit 2027 instalado: copia la DLL, `config.json` y el
   `.addin` a `%AppData%\Autodesk\Revit\Addins\2027\`.
3. En Revit: pestana ARBA > Acero > Losas. Seleccionar suelos estructurales de hormigon.
4. Si algo falla en Revit, el informe final y los avisos de la ventana dicen el motivo; las
   clases puras se depuran con `cd Tests && dotnet run`.

## Ideas pendientes (no implementadas)

- Muros portantes como apoyos de los bastones.
- Losas aligeradas en dos direcciones / reticulares.
- Ganchos distintos por extremo y longitudes de anclaje calculadas por diametro.
- Numeracion de particion por pano y etiquetas automaticas en planta.

## Entorno de compilacion usado en la sesion

`apt-get install dotnet-sdk-10.0` (Ubuntu 24.04, SDK 10.0.112) y `dotnet build` del proyecto
principal con `EnableWindowsTargeting=true`: WPF, los paquetes `Nice3point.Revit.Api.*` 2027.2 y
el codigo comun de `external/ARBA-comun` compilan en Linux (solo para comprobar; la DLL se usa
en Windows con Revit). En el `.csproj`, `<Compile Remove="external\**" />` antes del `Import`
del `.props` evita que el glob por defecto del SDK arrastre los tests y el proyecto de
comprobacion del submodulo (y duplique los archivos de `src/`).
