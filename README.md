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
  150 mm), lo avisa ya en la ventana; al armar, si el gancho queda dentro de la losa y
  sobresale, rechaza la losa con la medida real; si queda en la prolongación hacia la viga,
  solo avisa. Para ganchos más cortos: reducir la longitud de gancho del tipo de barra o
  usar otro tipo de gancho.
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
  usar las vigas como apoyos; plantilla del parámetro Partición (`{marca}`, `{id}`,
  `{tipo}`, `{familia}`, `{conjunto}`, `{capa}`).
- **Planta**: hormigón con huecos, vigas de apoyo (gris), franjas de las viguetas, cada
  barra a su grosor y con el color de su capa (rojo oscuro inferior, naranja bastones y
  superior principal, morado temperatura e inferior secundaria, azul superior secundaria),
  prolongaciones a trazos y marcas de gancho. Rueda: zoom; arrastrar: mover; doble clic:
  encajar. Al pasar el ratón por una barra se ve su capa, diámetro, posición y longitud.
- **Sección transversal** a media luz: espesor, ladrillos y viguetas con la losa superior,
  cada barra que cruza el corte como un círculo (las que no cruzan, como los bastones de
  apoyo, en hueco) y el acero perpendicular como una raya a su cota (con varios paños, un
  tramo por paño). Como la losa es muy
  ancha respecto a su espesor, al encajar se ven unas tres viguetas centradas: se puede
  ampliar, reducir y desplazar.
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
  "partitionTemplate": "LOSA-{marca}",
  "toleranceMm": 2, "minBarLengthMm": 300
}
```

Los nombres de tipo de barra y de gancho pueden ser exactos o un fragmento (`"1/2"`,
`"90"`); sin coincidencia no se arma, nunca se sustituye por otro tipo.

## Compilar e instalar

Requiere el SDK de .NET 10 y Revit 2027.2 (los paquetes `Nice3point.Revit.Api.*` 2027.2
traen las DLL de la API; para Revit 2025/2026 cambia el `TargetFramework` a
`net8.0-windows` y la versión del paquete).

```
dotnet build -c Debug
```

En Debug la compilación copia `SlabRebar.dll`, `config.json` y `SlabRebar.addin` a
`%AppData%\Autodesk\Revit\Addins\2027\`. Al abrir Revit aparece la pestaña **ARBA** con
el panel **Acero** y el botón **Losas** dentro del desplegable **Acero** (comparte la
pestaña y el desplegable con los add-ins de columnas y de muros si están instalados) y el
comando queda también en Complementos > Herramientas externas.

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
| `RevitTheme.cs` | Tema oscuro al estilo de Revit 2027 (el mismo que en columnas). |
| `ArmarLosaCommand.cs`, `RibbonApp.cs` | Comando externo y pestaña de la cinta. |
| `AppConfig.cs`, `PartitionName.cs` | Configuración y plantilla de Partición. |
| `Tests/` | Pruebas de consola de las clases puras. |
| `PLAN.md` | Plan de trabajo y estado del proyecto. |

Las clases puras (`Geometry2D`, `SlabPlan`, `AppConfig`, `PartitionName`) no dependen de
Revit y se prueban en el programa de consola.
