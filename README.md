# Uniones Acero – detallado de nudos viga-columna para Revit 2027

Complemento de Revit (C#, .NET 10, Revit 2027.x) que analiza el encuentro de una **columna de cualquier
sección** (rectangular, L, T, cruz, irregular) con las **vigas que llegan a ella**, decide cómo debe
anclarse cada capa de barras longitudinales (recto, gancho a 90° o barras pasantes), **verifica las
barras que ya estén modeladas y corrige las que no cumplen**, y genera barras nuevas solo donde no las hay.

## Cómo funciona, en cuatro pasos

1. **Lee el modelo.** Seleccionas una o varias columnas. De cada una se toma su sección real en planta
   (desde la geometría original de la familia, llevada a coordenadas del proyecto), su armadura ya
   modelada (barras verticales y estribos: el diámetro del estribo fija el núcleo confinado), las
   vigas que llegan a ella y las barras longitudinales que esas vigas ya tienen.
2. **Calcula el anclaje.** Para cada capa (superior e inferior) de cada viga se mide cuánta columna hay
   en la dirección de la viga y se compara con `ld` (recto) y `ldh` (gancho a 90°) de la norma elegida.
   Si hay una viga colineal al otro lado, las barras son pasantes.
3. **Decide qué hacer en cada capa.**
   - La viga **no tiene barras** en esa capa → se proponen **barras nuevas** con el tipo y número elegidos.
   - La viga **ya tiene barras** → se **verifica** cada conjunto. Si cumple, no se toca. Si no cumple
     (por ejemplo termina en la cara de la columna, o el gancho es corto), se propone **corregirlo**:
     la misma barra (mismo tipo, mismo número de barras, misma posición), conservada desde su extremo
     lejano, cortada en la columna y prolongada hasta el núcleo con gancho a 90° si el anclaje recto
     no cabe. En la ventana puedes cambiar esto a *solo verificar* o *ignorar y añadir barras nuevas*.
   - Si ni el recto ni el gancho caben, la capa queda **INSUFICIENTE** (en rojo) y no se toca.
4. **Armar.** Nada cambia hasta pulsar *Armar*. Entonces se crean las barras nuevas y se sustituyen las
   corregidas (se comprueba que cada gancho quede dentro de la columna; si no, se invierte, y si tampoco,
   se conserva la barra original y se avisa). El informe final dice qué se creó, qué se corrigió y qué no.

## Qué hace

Un solo botón, **Nudos**, en el desplegable **Acero** de la pestaña **ARBA** (la misma pestaña y el
mismo estilo que los add-ins de columnas y muros). Seleccionas una o varias columnas y se abre una
**ventana previa**: nada se crea hasta que pulsas *Armar*.

### La ventana

- **Columnas seleccionadas**: forma detectada (rectangular, en L, en T, en cruz…), tamaño, armadura
  modelada en la columna, número de vigas y el resumen del plan (barras nuevas, correcciones, existentes
  que cumplen, insuficientes); en rojo, el motivo si no se puede armar. Clic en una fila para ver su nudo.
- **Vigas de la columna seleccionada**: una fila por viga: incluirla o no, marcarla como *Ppal.*
  (principal en los cruces del nudo), tipo de barra y número de
  barras de cada capa (solo para las capas sin barras modeladas; vacío / 0 = valores generales), y el
  **resultado de cada capa**: `NUEVAS 3Ø16 gancho 90° (req. 272 / prov. 350)` en verde, `existentes 3Ø16
  cumplen` en verde, `existentes 3Ø16 NO cumplen → corregir: gancho 90°` en ámbar, o `INSUFICIENTE` en
  rojo. Al pasar el ratón se ve el detalle de cada conjunto de barras.
- **Vigas con barras modeladas** (desplegable): *verificar y corregir* (por defecto), *solo verificar* o
  *ignorar y añadir barras nuevas*. Los campos que solo sirven para barras nuevas (tipo y número por capa,
  extensión en la viga) se bloquean cuando la capa ya tiene barras modeladas.
- **Normativa y materiales**: ACI 318-19, NSR-10, E.060 o Eurocódigo 2; fy, f'c, nudo sísmico,
  concreto liviano, barras epóxicas.
- **Barras y gancho**: tipo de barra (RebarBarType) y cantidad por capa, tipo de gancho de 90°.
- **Recubrimientos y detallado**: recubrimientos y estribos de columna y viga (si el modelo no los
  define), llevar las barras hasta la cara lejana del núcleo o solo la longitud requerida, extensión
  dentro de la viga, holgura, redondeo, y las reglas de detallado del nudo (desplazar capas que se
  cruzan, correr barras para pasar por dentro de las verticales, retrasar ganchos en la misma esquina,
  empalmar barras pasantes fuera del nudo, varilla comercial).
- **Planta del nudo** (vista desde arriba, X a la derecha, Y arriba): la sección real de la columna con
  el núcleo a trazos, las vigas que llegan (las no conectadas, a trazos rojos con el motivo en el
  tooltip) y las barras: nuevas (superiores en rojo oscuro, inferiores a trazos naranja, pasantes en
  verde azulado, insuficientes en rojo con ✖), existentes a trazos (verde cumple, rojo no cumple, gris
  si se va a corregir) y corregidas en naranja; el punto al final es el gancho a 90°. Rueda: zoom;
  arrastrar: mover; doble clic: encajar; clic en una viga: su alzado. Al pasar el ratón por una barra
  se ve su anclaje requerido y provisto.
- **Alzado de la viga seleccionada**: corte por el eje de la viga, con la viga a la izquierda entrando
  en la columna por la derecha: límite del núcleo, barras nuevas y corregidas con su gancho (hacia abajo
  las superiores, hacia arriba las inferiores) y las cotas de anclaje provisto y requerido (ld o ldh),
  y las existentes a trazos con su veredicto. Si la viga no está conectada se explica por qué.
- Las **barras existentes que sigan sin cumplir** (modo *solo verificar* o sin corrección posible) se
  pueden marcar en rojo en la vista activa al armar.
- **Guardar como valores por defecto** escribe `config.json`; **Armar** crea las barras;
  **Cancelar** no toca nada.

### Al armar

Cada columna se arma en una subtransacción. Las capas con anclaje insuficiente **no se crean ni se
corrigen** (quedan en rojo en la ventana para que cambies diámetro, cantidad o columna). Después de crear
cada barra con gancho se lee su geometría real: si el gancho asoma fuera de la columna se invierte la
orientación y se vuelve a crear; si sigue fuera, se descarta esa barra (en una corrección se conserva la
original) y el informe final lo dice. Las barras nuevas se crean como conjuntos (array) alojados en la
viga, con el tipo de barra y de gancho elegidos.

**Corrección de una barra existente**: se crea una barra nueva con el mismo tipo, el mismo anfitrión, la
misma normal y la misma distribución (número de barras y longitud del conjunto), con el eje de la original
desde su extremo lejano hasta la columna y el nuevo anclaje, y después se borra la original. Si la barra
tenía gancho en el extremo lejano, ese gancho se conserva como tramo de la barra. Las barras repartidas en
una dirección que no es la lateral de la viga no se corrigen automáticamente (se avisa). Si una misma viga
llega a dos columnas seleccionadas, la segunda corrección se replantea sobre la barra ya corregida.
La barra corregida es un elemento nuevo (su id sale en el informe): hereda de la original las vistas en las
que se mostraba *sin obstrucción* y, como las barras nuevas, se muestra sin obstrucción en la vista activa
(en Revit ese estado es por barra y por vista y no se copia solo; sin él, una barra nueva queda oculta
dentro del hormigón en las vistas 3D sombreadas y parece borrada).

## Detallado del nudo como en obra

Una vez decidido el anclaje de cada capa, el plugin resuelve los choques que el fierrero resolvería al
armar, sobre las barras nuevas, las corregidas y, como obstáculos fijos, las existentes que se conservan:

1. **Las barras de la viga pasan por dentro de las verticales de la columna.** Se leen las posiciones de
   las barras verticales de la columna (si no hay, se supone una en cada esquina del núcleo). La barra de
   viga que coincide con una vertical se corre en planta hacia el eje de la viga, toda ella, hasta dejar
   la separación libre configurada (25 mm). Si el ancho de la viga no da, se admite el contacto con la
   vertical y, en último caso, con las otras barras de la viga, y se avisa. Un conjunto existente se corre
   como conjunto (su primera barra y su longitud de reparto).
2. **Ganchos en la misma esquina.** Cuando el gancho de una viga atravesaría las barras o los ganchos de
   la otra, el extremo de la viga secundaria retrocede, sin bajar del anclaje requerido, hasta que queda
   libre. Si la secundaria no puede, cede la principal; si ninguna puede con 25 mm libres se prueba en
   contacto; y si tampoco, se avisa para resolverlo en obra.
3. **Cruces a la misma cota.** Si tras lo anterior las barras de dos vigas aún se cruzan en planta a la
   misma cota, la capa de la viga secundaria baja (superior) o sube (inferior) un diámetro más 25 mm
   para pasar por debajo o por encima de la principal.
4. **Barras pasantes ya modeladas (vigas colineales).** No se anclan las dos en el nudo: la barra de la
   viga principal se hace continua a través de la columna y se empalma por traslape con la de la viga
   opuesta fuera del nudo, a partir de 2h de la cara (ACI 318-19 18.6.3.3, E.060 21.5.2.3), con una
   bayoneta de pendiente 1:6 justo antes del traslape; la barra opuesta se corta en el inicio del
   traslape. lst = traslape clase B (con diámetros distintos, el mayor de ld del grueso y lst del fino).
   Se avisa si la barra continua supera la varilla comercial (9 m) o si la opuesta es demasiado corta.

**Viga principal**: la marcada como *Ppal.* en la tabla de vigas; sin marcar, la de mayor peralte, luego
mayor ancho, luego barras de mayor diámetro y, por último, la primera de la lista (para los ganchos, a
igualdad, cede la viga con más holgura de anclaje). Cada ajuste aparece en la tabla de vigas, en los
esquemas y en el comentario de la barra creada; lo que no se pudo resolver sale como aviso. Todo esto se
puede desactivar en *Recubrimientos y detallado*.

## Cómo decide el anclaje

Para cada viga que llega a la columna:

1. Se detecta en qué cara (arista del polígono) apoya la viga y se lanza el eje de la viga a través de
   la sección para obtener la **profundidad disponible** hasta la cara opuesta. En una T, por ejemplo, una
   viga que llega al extremo del ala tiene toda la longitud del ala, pero una viga que llega a la cara
   frontal del ala fuera del alma solo tiene el espesor del ala.
2. Profundidad útil = disponible − recubrimiento − Ø estribo − holgura (el gancho debe quedar dentro del
   núcleo confinado).
3. Si hay una viga colineal al otro lado, las barras son **pasantes** (se comprueba `h_col ≥ 20·db`).
4. Si la profundidad útil ≥ `ld`: **anclaje recto**. Si ≥ `ldh`: **gancho a 90°** (superiores hacia abajo,
   inferiores hacia arriba; si la columna termina en la viga, ambos hacia abajo). Si no cabe ninguno:
   **INSUFICIENTE** y se indica cuánto falta.
5. Después se aplican las reglas de detallado del nudo (ver arriba): verticales de la columna, ganchos
   en la misma esquina y cruces a la misma cota.
6. Se avisa si la viga sobresale de la cara de la columna, llega oblicua, no la toca, o si dos vigas llegan
   por la misma cara en la misma posición (viga duplicada).

## Estructura

```
UnionesAcero.sln
├─ src/UnionesAcero.Core      Núcleo geométrico y normativo. Sin dependencia de Revit (net10.0).
│   ├─ Geometry/              Vec2/Vec3, Segment2D, Polygon2D (recorte de rectas, offset interior…)
│   ├─ Model/                 ColumnSection, BeamSection, Materials, JointOptions
│   ├─ Codes/                 IAnchorageCode: Aci318Code, Nsr10Code, E060Code, Eurocode2Code
│   ├─ Analysis/              JointAnalyzer → JointAnalysis (BeamJoint, LayerResult, BarGroup); JointDetailer (choques en el nudo)
│   ├─ Verification/          ExistingBarChecker (verifica barras ya modeladas), BarFixer (propone su corrección), SpliceResolver (empalme de pasantes)
│   └─ Reporting/             ReportFormatter (informes en texto)
├─ src/UnionesAcero.Revit     Adaptador Revit 2027 (net10.0-windows)
│   ├─ Ribbon/ArbaRibbon.cs   Pestaña ARBA, panel y desplegable Acero (compartidos con los otros add-ins)
│   ├─ Commands/NudosCommand  Selección, análisis, ventana y creación de barras
│   ├─ Services/              Lectura de geometría (columna, vigas, barras existentes), RebarBuilder
│   ├─ UI/                    JointWindow, PlanPreview, ElevationPreview, RevitTheme (WPF en código)
│   └─ config.json            Valores por defecto de la ventana
└─ tests/UnionesAcero.Core.Tests   xUnit (49 pruebas: geometría, normas, analizador, verificador, corrector, detallado, empalmes)
```

El núcleo se puede reutilizar desde otro CAD (AutoCAD, Tekla) escribiendo solo otro adaptador.

## Compilar e instalar

Requisitos: Revit 2027.x, SDK de .NET 10 y Visual Studio 2022 17.14+ (o `dotnet` CLI).

```powershell
git clone <este repositorio>
cd Uniones
dotnet build -c Release
```

Al compilar en Windows, el proyecto copia automáticamente `UnionesAcero.Revit.dll`, `UnionesAcero.Core.dll`,
`config.json` (solo si no existe ya) y `UnionesAcero.addin` en `%AppData%\Autodesk\Revit\Addins\2027\`.
Abre Revit y aparecerá el botón **Nudos** en el desplegable **Acero** de la pestaña **ARBA** (comparte
pestaña y desplegable con Columnas y Muros si están instalados). También queda en Complementos >
Herramientas externas. Para desactivar la copia: `dotnet build -p:DeployToRevit=false`.

Las referencias a la API vienen de los paquetes NuGet `Nice3point.Revit.Api.RevitAPI/RevitAPIUI 2027.2.0`
(solo referencia, no se copian). Para otra versión 2027.x cambia `RevitApiPackageVersion` en el `.csproj`.

Pruebas del núcleo:

```powershell
dotnet test
```

## Requisitos del modelo

- Columnas y vigas como instancias de familia de las categorías *Pilares estructurales* y *Armazón estructural*.
- Vigas rectas (línea de ubicación `Line`). Las curvas se omiten con aviso.
- Columnas verticales. La sección se toma de la cara horizontal superior del sólido original de la familia
  (`GetOriginalGeometry`, que Revit devuelve en coordenadas de la familia y el plugin transforma al proyecto).
- Para armar, el proyecto necesita tipos de barra (`RebarBarType`), un tipo de gancho estándar de 90° y
  formas de armadura cargadas; la ventana no deja armar sin tipo de gancho si hay anclajes con gancho.
- Recubrimientos: se leen del elemento (`RebarHostData`) y si no, de la configuración.
- Barras de las vigas: si la viga ya tiene barras longitudinales modeladas se verifican (y se corrigen las
  que no cumplen); para las capas sin barras se usan el tipo y la cantidad de la ventana (o, si se marca
  la casilla, los de las barras del modelo).
- Armadura de la columna: si tiene estribos modelados se usa su diámetro para el núcleo confinado; si no,
  el de la configuración. Las posiciones de sus barras verticales se usan para correr las barras de las
  vigas; si no hay, se supone una vertical en cada esquina del núcleo.

## Hoja de ruta

- **Anclaje de columnas en cimientos corridos y zapatas** (patillas en la base de la columna):
  idea, diseño y plan de trabajo en [docs/PLAN-cimientos.md](docs/PLAN-cimientos.md). No implementado.

## Limitaciones y cosas a validar en obra/oficina

- Es una herramienta de **detallado geométrico**: no diseña el refuerzo ni verifica cortante en el nudo,
  cuantías ni confinamiento. El ingeniero responsable debe revisar los resultados. En particular, bajar
  una capa para cruzar reduce el brazo de palanca de esa viga, y los avisos de "en contacto" o "no cabe"
  indican nudos congestionados que conviene revisar (menos barras, paquetes o mayor sección).
- Las fórmulas están implementadas en MPa con los factores habituales (ψt, ψe, ψg, ψc, λ; α1, α2 en EC2).
  Los casos especiales (barras con cabeza, paquetes de barras, ganchos de 180°) no están cubiertos.
- La orientación del gancho se calcula con la convención `RebarTerminationOrientation` de la API 2026+
  (derecha = dirección × normal) y se comprueba después de crear la barra: se localiza la pata del gancho
  comparando el eje con y sin ganchos (Revit puede devolver el recorrido invertido, con el gancho al
  principio) y se verifica que su extremo y su centro queden dentro de la sección de la columna y entre
  su base y su coronación; si queda fuera se invierte automáticamente.
- La extensión del gancho la fija el tipo de gancho de Revit; si difiere de la que pide la norma
  (12·db a 90° en ACI) el informe lo avisa.
