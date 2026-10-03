# Armado automático de losas — add-in Revit 2027

Genera la armadura de **losas aligeradas** (viguetas con ladrillo de techo y losa
superior) y de **losas macizas** a partir de la **geometría real del elemento**, sin
depender de los nombres de parámetros de tus familias:

- **Aligerada**: barra inferior de cada vigueta (1 o 2), **bastones** superiores en los
  apoyos (o barra superior corrida) y **acero de temperatura** en la losa superior,
  perpendicular a las viguetas.
- **Maciza**: malla inferior en la dirección principal y en la secundaria (la de
  temperatura/repartición en losas en una dirección) y, opcional, malla superior corrida o
  bastones en los apoyos, con su secundaria.

Es el hermano del add-in de columnas
([Acero-columnas](https://github.com/Andy-rba30/Acero-columnas)) y del de muros
([Acero-automatico](https://github.com/Andy-rba30/Acero-automatico)): misma base (lectura
del sólido, ventana previa con esquemas y tema oscuro de Revit, red de seguridad que
deshace el elemento entero si una barra queda fuera del hormigón, `config.json`) y el
mismo botón en la pestaña **ARBA** > panel **Acero** > desplegable **Acero** > **Losas**.

Comparte con todos los add-ins ARBA el código común de
[ARBA-comun](https://github.com/Andy-rba30/ARBA-comun) (contrato **1.0.0**, submódulo
`external/ARBA-comun`): la cinta, el tema oscuro, la plantilla de partición, los parámetros
compartidos `ARBA - Origen` / `ARBA - Código` / `Metrado - Elemento`, y las reglas de borrar y
rearmar y de migración de modelos antiguos (ver [Contrato ARBA-comun](#contrato-arba-comun)).

Antes de crear nada abre una **ventana** en la que se ve qué se ha detectado en cada losa
seleccionada y se elige el armado: tipo de losa y dirección (general y por losa), viguetas,
tipos de barra, separaciones, bastones, temperatura y recubrimientos, con un esquema en
**planta** y de la **sección transversal**.

## Cómo lee la losa

1. Toma el sólido del suelo (`Floor`). Tiene que tener **una única cara superior plana y
   horizontal**: losas inclinadas, escalonadas, con rebajes o partidas se rechazan con un
   mensaje claro y sin crear ninguna barra.
2. De esa cara salen los **contornos exteriores y los huecos** (los bordes curvos se teselan).
   Un mismo suelo puede tener **varios paños** separados por vigas (varios contornos cerrados
   en el boceto): cada paño se arma por separado y sus bordes cuentan como bordes exteriores.
   El espesor es la distancia a la cara inferior; si no coincide con el del tipo (losa
   cortada o unida a otros elementos) se avisa y se usa el sólido.
3. Busca las **vigas** (armazón estructural de eje recto) a la cota de la losa, tanto las
   enrasadas con la cara superior como las que la sostienen por debajo, recentra su eje en
   su sólido y mide su ancho.
4. Elige los **ejes locales**: `u` es la dirección de las viguetas (aligerada) o de las
   barras principales (maciza); `v` es la perpendicular. Por defecto `u` va paralela al
   **lado corto** de la losa (las viguetas cubren la luz menor); también puede ir por el
   lado largo, por los ejes X o Y del proyecto o con un ángulo. Se cambia en general y
   **losa a losa** desde la lista de la ventana.
5. Las vigas casi perpendiculares a `u` (3°) son **apoyos**; las paralelas u oblicuas se
   ignoran y se listan en el informe.

El **tipo de losa** se decide por el nombre del tipo de suelo (si contiene `aliger` es
aligerada; si no, maciza), o se fija en general o losa a losa en la lista.

## Reglas de armado (`SlabPlan`)

Cada barra es una recta `v = cte` (a lo largo de `u`) o `u = cte` (a lo largo de `v`),
**recortada contra el contorno con sus huecos**: cada tramo interior es una barra. Vale
para cualquier forma (rectangular, en L, con huecos, irregular). Además la franja de
ancho `recubrimiento + diámetro` alrededor de la recta tiene que quedar dentro del
hormigón, así la barra guarda el recubrimiento con los bordes paralelos a ella.

- En el **borde exterior** la barra para al recubrimiento lateral o, si se da una
  **prolongación**, sobresale esa longitud hacia la viga (anclaje). En ese extremo puede
  llevar **gancho**, con o sin prolongación (hacia arriba en las capas inferiores, hacia
  abajo en las superiores: el plugin crea la primera barra, lee hacia dónde dobla el gancho
  y si es al revés la borra y la vuelve a crear con la otra orientación). Sin prolongación
  el gancho queda **dentro de la losa**: el tramo recto acaba antes (radio de doblado más un
  diámetro) para que sea la cara exterior del gancho la que guarde el recubrimiento. En los
  **huecos** siempre para al recubrimiento y sin gancho.
- El **tamaño del gancho** no lo decide el plugin: lo fija Revit en el tipo de barra
  (Editar tipo > Longitudes de gancho, por tipo de gancho; por defecto el multiplicador del
  gancho, p. ej. 12 diámetros para 90°). El plugin lee esa longitud y, si no cabe entre la
  barra y el recubrimiento opuesto (en una losa de 200 mm con 25 de recubrimiento caben
  150 mm), **acerca la barra a su propia cara lo que falte** (sin dejar menos de un
  diámetro, y nunca menos de 10 mm, de recubrimiento propio; los bastones bajo la
  temperatura y las capas secundarias que descansan sobre otra no se mueven) y lo avisa ya
  en la ventana con la nueva cota. Si ni así cabe, el gancho invade el recubrimiento
  opuesto y solo se avisa: la losa se arma igualmente y únicamente se rechaza si el gancho
  llegara a salirse del hormigón (lo detecta la comprobación de la geometría real). Para
  ganchos más cortos: reducir la longitud de gancho del tipo de barra o usar otro tipo de
  gancho **de estilo Estándar**. Los ganchos de estilo *Estribo/Tirante* no se ofrecen en
  la ventana: todas las barras de losa son de estilo Estándar y Revit no admite en ellas un
  gancho de estribo (falla al crear la barra con "internal error").
- Las barras iguales y equiespaciadas se crean como **un solo conjunto de Revit (array)**,
  igual que si se modelaran a mano (la fila de barras inferiores de un paño rectangular es
  un conjunto; si hay dos barras por vigueta, dos conjuntos entrelazados).
- Los tramos más cortos que `minBarLengthMm` (300 mm) se omiten y se cuentan en el aviso.

### Losa aligerada

- **Viguetas** de ancho `joistWidthMm` (100) cada `joistSpacingMm` (400), a lo largo de
  `u` y repartidas en `v`: reparto centrado en la losa o desde una distancia dada al
  borde (`firstJoistOffsetMm`). Toda vigueta queda entera dentro de la losa.
- **Barra inferior** por vigueta (1, o 2 lado a lado; se avisa si no caben con 25 mm
  libres), a `recubrimiento inferior + medio diámetro` de la cara inferior.
- **Bastones**: en cada **apoyo** que cruza la vigueta. Apoyo extremo = la viga que toca el
  extremo del tramo (o el propio borde si no hay viga): el bastón sale de la cara interior
  de la viga una longitud `endFraction × luz libre` (L/5) y hacia fuera lleva la
  prolongación y el gancho. Apoyo interior = viga dentro del tramo: bastón
  `interiorFraction × luz libre` (L/4) a cada lado, medido desde cada cara de la viga. Una
  **longitud fija** (`fixedLengthMm`) anula las fracciones. Los bastones que se solapan se
  unen. En los extremos que dan a un hueco no hay bastón. También pueden ser una **barra
  corrida** o **ninguna**.
- **Acero de temperatura**: barras a lo largo de `v` (perpendiculares a las viguetas) en
  la losa superior, cada `spacingMm` (250) como máximo, repartidas por igual. Es la capa
  **más alta**: al recubrimiento superior (recubrimiento + medio diámetro) o a la
  profundidad `depthMm` que se indique; los bastones o la barra corrida van **justo por
  debajo** de ella para cruzarse sin chocar. Se avisa si queda por debajo de la losa
  superior.

### Losa maciza

- **Inferior principal** a lo largo de `u`, repartida en `v` cada `spacingMm` como máximo;
  es la capa más baja. **Inferior secundaria** (temperatura / repartición) a lo largo de
  `v`, apoyada encima.
- **Superior principal** opcional: bastones en los apoyos (misma regla que la aligerada,
  línea a línea), corrida o ninguna. **Superior secundaria** opcional, corrida, colgada
  por debajo de la principal.

En ambos casos la separación es un **máximo**: `n = techo(L / s)` huecos iguales, con
barra en los dos extremos al recubrimiento.

## Comprobaciones de seguridad

Igual que en columnas y muros: **o se arma la losa entera y bien, o no se arma**.

1. **Antes de crear cada conjunto** se comprueba que el eje de la barra y cuatro fibras
   desplazadas medio diámetro (en horizontal perpendicular y en vertical) quedan dentro
   del hormigón (`Solid.IntersectWithCurve`) en todas las posiciones del array. Solo se
   comprueba el **tramo dentro del contorno**: la prolongación hacia la viga es la única
   parte de barra que puede quedar fuera de la losa. Si se usan las vigas como apoyos, su
   hormigón también cuenta como válido (losa con muescas por uniones).
2. **Después de crear y regenerar** se lee la geometría real de cada barra de cada
   conjunto (ganchos y radios incluidos) y se vuelve a comprobar. Un gancho que para dentro
   de la losa se comprueba entero (el tramo comprobable se abre por ese extremo); el que va
   en la prolongación hacia la viga, no.
3. Cualquier fallo deshace la subtransacción de ese elemento: no queda ni una barra.

El informe final dice, losa a losa, qué se ha creado (barras por capa y conjuntos) y por
qué se ha rechazado lo que no.

## Interfaz gráfica

- **Losas seleccionadas**: tipo detectado, dimensiones `u × v`, dirección, espesor,
  huecos, vigas cercanas y el resumen del armado (o, en rojo, el motivo del rechazo). Cada
  fila armable tiene su **tipo** (general / aligerada / maciza) y su **dirección** propios.
  Clic en una fila para verla en los esquemas.
- **Tipo de losa y dirección**: tipo por defecto, dirección general y ángulo. Solo se
  muestran las entradas del tipo de losa que se va a armar (aligerada y temperatura, o
  maciza), según el tipo general, el propio de cada losa o el deducido por el nombre.
- **Losa aligerada**: ancho y separación de viguetas, losa superior, primera vigueta;
  barra inferior (tipo, 1 o 2, prolongación, gancho); barras superiores (bastones /
  corrida / ninguna, tipo, L/4 y L/5 o longitud fija, prolongación, gancho).
- **Acero de temperatura**: activar, tipo, separación, profundidad, prolongación.
- **Losa maciza**: malla inferior (principal y secundaria) y malla superior (principal con
  bastones / corrida / ninguna y secundaria).
- **Recubrimientos, apoyos y partición**: recubrimiento inferior, superior y de bordes;
  usar las vigas como apoyos; plantilla del parámetro Partición (`{categoria}`, `{prefijo}`,
  `{marca}`, `{id}`, `{codigo}` o `{capa}`, `{tipo}`, `{familia}`, `{conjunto}`) con el
  ejemplo de la losa seleccionada y un aviso en rojo si la plantilla no empieza por
  `{categoria} - {prefijo}-` como exige el contrato. El pie de la ventana muestra la versión
  del contrato ARBA-comun con la que se compiló.
- **Planta**: hormigón con huecos, vigas de apoyo (gris), franjas de las viguetas, cada
  barra a su grosor y con el color de su capa (rojo oscuro inferior, naranja bastones y
  superior principal, morado temperatura e inferior secundaria, azul superior secundaria),
  prolongaciones a trazos y marcas de gancho. Rueda: zoom; arrastrar: mover; doble clic:
  encajar. Al pasar el ratón por una barra se ve su capa, diámetro, posición y longitud.
- **Sección transversal** a media luz, como el detalle típico de un plano: no se dibuja la
  losa entera (saturaba el esquema) sino un módulo con marcas de corte a los lados (en la
  aligerada dos viguetas del centro con el ladrillo entre ellas y medio ladrillo a cada
  lado; en la maciza un metro), con sus cotas en mm: espesor, losa superior y altura del
  ladrillo, anchos de vigueta y ladrillo, recubrimientos superior e inferior. Cada barra
  que cruza el corte es un círculo (las que no cruzan, como los bastones de apoyo, en
  hueco) y el acero perpendicular una raya a su cota con su rótulo de directriz (capa,
  diámetro y separación); en la cabecera, cada capa que cruza el corte con su diámetro y
  su cota desde la cara inferior. Se puede ampliar, reducir y desplazar.
- **Guardar como valores por defecto** escribe `config.json`; **Armar** crea las barras;
  **Cancelar** no toca nada.

Sin tipo de barra elegido los esquemas se dibujan con diámetros orientativos y el botón
Armar avisa de qué falta.

## Limitaciones

- Solo losas **horizontales y de espesor único**: las inclinadas, escalonadas o con rebajes se
  rechazan.
- Los apoyos son **vigas de eje recto** casi perpendiculares a `u`; los muros portantes y
  las vigas oblicuas o curvas no se detectan (sin vigas, los apoyos son los extremos de
  la losa). Si la losa está modelada **entre** las caras de las vigas, usa la prolongación
  para anclar en ellas; si está modelada **sobre** las vigas, déjala en 0.
- La aligerada es **en una dirección**; no se arman losas reticulares ni aligeradas en dos
  direcciones. El sólido del suelo es un prisma (los ladrillos no están modelados), así que
  la comprobación de hormigón no puede detectar una barra en la zona del ladrillo: es el
  plan el que las mantiene en las viguetas y en la losa superior.
- No se comprueban choques entre barras de capas distintas más allá del apilado de cotas.

## config.json

```jsonc
{
  "coverBottomMm": 25, "coverTopMm": 25, "coverEdgeMm": 25,
  "kind": "auto",                                  // auto (por el nombre del tipo) | aligerada | maciza
  "direction": { "mode": "short", "angleDeg": 0 }, // short | long | x | y | angle
  "aligerada": {
    "joistWidthMm": 100, "joistSpacingMm": 400, "topSlabMm": 50, "firstJoistOffsetMm": 0,
    "bottom":      { "barTypeName": "", "count": 1, "extensionMm": 0, "hookTypeName": "" },
    "top":         { "mode": "bastones", "barTypeName": "", "interiorFraction": 0.25, "endFraction": 0.2,
                     "fixedLengthMm": 0, "extensionMm": 0, "hookTypeName": "" },
    "temperature": { "enabled": true, "barTypeName": "", "spacingMm": 250, "depthMm": 0, "extensionMm": 0 }
  },
  "maciza": {
    "bottomMain":      { "barTypeName": "", "spacingMm": 200, "extensionMm": 0, "hookTypeName": "" },
    "bottomSecondary": { "enabled": true, "barTypeName": "", "spacingMm": 250, "extensionMm": 0 },
    "topMain":         { "mode": "ninguna", "barTypeName": "", "spacingMm": 200, "interiorFraction": 0.25,
                         "endFraction": 0.2, "fixedLengthMm": 0, "extensionMm": 0, "hookTypeName": "" },
    "topSecondary":    { "enabled": false, "barTypeName": "", "spacingMm": 250, "extensionMm": 0 }
  },
  "detectBeams": true,
  "partitionTemplate": "{categoria} - {prefijo}-{marca}",   // LOSAS - LOS-L2 (contrato ARBA-comun)
  "toleranceMm": 2, "minBarLengthMm": 300
}
```

Los nombres de tipo de barra y de gancho pueden ser exactos o un fragmento (`"1/2"`,
`"90"`); sin coincidencia no se arma, nunca se sustituye por otro tipo.

## Contrato ARBA-comun

El add-in sigue el contrato de [ARBA-comun](https://github.com/Andy-rba30/ARBA-comun)
(`external/ARBA-comun/CONTRATO.md`, versión **1.0.0**), igual que el resto de add-ins ARBA y el
plugin de metrados:

- **Partición** de cada conjunto: `LOSAS - LOS-{marca}` (p. ej. `LOSAS - LOS-L2`; sin Marca, el
  Id de la losa). La categoría `LOSAS` la deduce el común de la categoría del anfitrión (suelos);
  el prefijo `LOS` identifica a este add-in (antes era `LOSA-{marca}`). La capa **no** va en la
  partición: así el plugin de metrados agrupa el acero por losa en "Metrado acero - Losas".
  La plantilla se puede cambiar en `config.json` o en la ventana, pero tiene que empezar por
  `{categoria} - {prefijo}-` o la ventana lo avisa.
- **Parámetros compartidos** (de ejemplar, grupo Datos, GUID fijo; el add-in los crea y vincula
  en el proyecto la primera vez que arma, sin tocar el archivo de parámetros compartidos del
  usuario): `ARBA - Origen` = `LOSAS`, `ARBA - Código` = capa (`inferior`, `baston`,
  `temperatura`, `inferior-sec`, `superior`, `superior-sec`) y `Metrado - Elemento` = `LOSAS`.
- **Borrar y rearmar**: si alguna losa seleccionada ya tiene conjuntos con `ARBA - Origen =
  LOSAS`, antes de armar pregunta una vez: **borrar la armadura del add-in y rearmar** (solo lo
  que creó Losas; si la losa se rechaza, su armadura anterior se conserva), **conservar y armar
  encima** (queda duplicado) o cancelar.
- **Migración** de modelos anteriores al contrato: las barras con partición `LOSA-…` y sin
  origen no se reconocen como propias, así que el add-in ofrece **migrarlas** (a `LOSAS -
  LOS-…`, con `ARBA - Origen`, `ARBA - Código` y `Metrado - Elemento`, sin crear ni borrar
  barras), bien antes de borrar y rearmar, bien con **Migrar sin rearmar**. El botón "Migrar
  particiones y origen" para todo el modelo vive en el plugin de metrados.
- El informe final y el pie de la ventana indican la versión del contrato con la que se compiló.

## Compilar e instalar

Requiere el SDK de .NET 10 y Revit 2027.2 (los paquetes `Nice3point.Revit.Api.*` 2027.2
traen las DLL de la API; para Revit 2025/2026 cambia el `TargetFramework` a
`net8.0-windows` y la versión del paquete).

```
git clone --recurse-submodules https://github.com/Andy-rba30/Acero-losas
# en un clon ya hecho sin el submodulo:
git submodule update --init
dotnet build -c Debug
```

El código común se compila **como fuente dentro de `SlabRebar.dll`** (clases `internal`,
namespace `Arba.Comun`) desde el submódulo `external/ARBA-comun` (etiqueta `v1.0.0`), nunca como
DLL compartida: Revit carga todos los add-ins a la vez y dos versiones de una misma DLL
chocarían. No se edita nada dentro de `external/ARBA-comun` desde este repo; para subir de
versión, `git -C external/ARBA-comun checkout vX.Y.Z` y commit del puntero.

En Debug la compilación copia `SlabRebar.dll`, `config.json` y `SlabRebar.addin` a
`%AppData%\Autodesk\Revit\Addins\2027\`. Al abrir Revit aparece la pestaña **ARBA** con
el panel **Acero** y el botón **Losas** dentro del desplegable **Acero** (comparte la
pestaña, los paneles y el desplegable con todos los add-ins ARBA instalados: zapatas,
cimientos, bloques, vigas, columnas, muros y el plugin de metrados) y el comando queda también
en Complementos > Herramientas externas.

El proyecto lleva `EnableWindowsTargeting`, así que también compila en Linux o macOS
para comprobar el código (la DLL solo sirve en Windows con Revit). Las clases puras se
prueban sin Revit con el programa de consola de `Tests/`:

```
cd Tests && dotnet run
```

## Estructura del código

| Archivo | Qué hace |
|---------|----------|
| `Geometry2D.cs` | Geometría pura: contorno con huecos, recorte scan-line de una recta con franja de recubrimiento, reparto de posiciones, ejes de viguetas, unión de tramos. |
| `SlabPlan.cs` | Armado de la losa (viguetas, barras por capa, bastones por apoyos, temperatura, conjuntos). Pura, compartida por ventana y generador. |
| `SlabOutline.cs` | Lectura del sólido de Revit: cara superior, contorno, espesor, vigas; sistema local por dirección (`SlabFrame`). |
| `HostAnalysis.cs` | Resultado por elemento (contorno o motivo de rechazo) y elecciones por losa (tipo, dirección). |
| `RebarGenerator.cs` | Crea los `Rebar` con las dos redes de seguridad y la orientación automática de ganchos. |
| `RebarOptionsWindow.cs`, `PlanPreview.cs`, `SectionPreview.cs` | Ventana y esquemas (WPF en código, sin XAML). |
| `ArmarLosaCommand.cs` | Comando externo: selección, análisis, ventana, parámetros compartidos del contrato, borrar / conservar / migrar la armadura previa, transacción e informe. |
| `RibbonApp.cs` | Botón **Losas** (icono propio) en la cinta común `ArbaRibbon`. |
| `AppConfig.cs` | Configuración (`config.json`) y plantilla de Partición por defecto del contrato. |
| `external/ARBA-comun/` | Submódulo con el código común ARBA: contrato (`ArbaContract`), partición (`ArbaPartition`, `PartitionName`), parámetros compartidos (`ArbaSharedParams`), origen (`ArbaOrigin`), migración (`ArbaMigration`), cinta (`ArbaRibbon`), tema oscuro (`RevitTheme`) y regla de nombres (`NameMatch`). No se edita desde este repo. |
| `Tests/` | Pruebas de consola de las clases puras. |
| `PLAN.md` | Plan de trabajo y estado del proyecto. |

Las clases puras (`Geometry2D`, `SlabPlan`, `AppConfig` y, del común, `ArbaContract`,
`ArbaPartition`, `PartitionName`, `NameMatch`) no dependen de Revit y se prueban en el programa
de consola.
