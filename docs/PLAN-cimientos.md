# Plan: anclaje de columnas en cimientos corridos y zapatas

Estado: **idea, no implementado**. Este documento recoge qué haría la función, cómo encaja en el
código actual y en qué orden se construiría. Nada de lo descrito aquí existe todavía en el plugin.

## 1. La idea

Hoy el plugin resuelve el nudo viga-columna: una barra horizontal de viga que entra en la columna y
necesita anclaje recto, gancho a 90° o pasar de largo. En la base de la columna ocurre lo mismo con
los papeles cambiados: las barras verticales de la columna bajan al cimiento (zapata aislada,
cimiento corrido bajo muros de albañilería, losa de cimentación) y terminan en una **patilla** (doblez
horizontal a 90°). Hay que comprobar que:

1. La barra **no atraviesa el cimiento**: el exterior de la patilla queda por encima del fondo con su
   recubrimiento (70 mm en contacto con el terreno según E.060; 75 mm en ACI 318). En el modelo de la
   garita se ve que varias patillas asoman por debajo del cimiento corrido: es el primer error que
   esta función detectaría.
2. La **longitud de anclaje vertical** cabe en el espesor del cimiento bajo la columna: ld o ldh si
   la barra trabaja a tracción (columna de pórtico sísmico), ldc si solo trabaja a compresión
   (columna de confinamiento de albañilería). En compresión el gancho no cuenta como anclaje.
3. La **patilla queda dentro del cimiento en planta**, con recubrimiento a su borde. En un cimiento
   corrido de 400-500 mm de ancho con una columna en el borde (esquinas, columnas en L), la pata de
   12·db hacia fuera sale del hormigón. La corrección es girarla hacia el eje del cimiento.
4. En zapatas armadas, la patilla **apoya sobre la parrilla inferior** y no la atraviesa.
5. (Opcional) Las patillas de barras de caras distintas **no chocan** entre sí dentro del cimiento.

Y, como con las vigas, corregir lo que no cumple sin rehacer el armado: la misma barra, cortada a la
cota correcta y con la patilla girada hacia donde hay hormigón.

## 2. Qué se reutiliza

| Ya existe | Se reutiliza para |
|---|---|
| `Polygon2D` (sección de cualquier forma, `RayDepth`, `InwardOffset`, `LineClip`) | Sección en planta del cimiento y distancia desde cada barra al borde en cada dirección |
| `IAnchorageCode` (ld, ldh, extensión del gancho, diámetro de doblado) | Mismo cálculo; falta añadir **ldc** (anclaje en compresión) |
| `ExistingBarChecker` / `BarFixer` (verificar y reconstruir una barra existente) | Mismo patrón con el eje vertical y el gancho horizontal |
| `RebarInspector.Describe` (eje con y sin ganchos de un conjunto) | Lectura de las barras verticales de la columna |
| `RebarBuilder.CreateFixed` (barra nueva con el tipo, anfitrión y distribución del conjunto original, luego se borra el original) | Sustituir el conjunto de barras de la columna |
| `RevitGeometryExtractor.LargestSolid` (geometría original transformada al proyecto) | Sólido del cimiento cuando sea una familia |
| `JointWindow` (lista de columnas, planes por capa, previsualizaciones) | Un grupo más "Cimiento bajo la columna" y un tercer esquema |

## 3. Diseño

### 3.1 Núcleo (`UnionesAcero.Core`, sin Revit, con pruebas)

Nuevos tipos en `Model/`:

- `FootingSection`: contorno en planta (`Polygon2D`) a la cota de la patilla, cota superior, cota
  inferior (constante o por punto, para cimientos escalonados), recubrimiento al terreno, y si es
  armada: diámetro y cota de la parrilla inferior.
- `ColumnBarSet`: diámetro, posiciones en planta de cada barra del conjunto, cota del extremo
  inferior, si tiene patilla y su dirección y longitud. Un `ColumnBarSet` por elemento `Rebar`.
- `FootingOptions`: barras en tracción o compresión, recubrimiento inferior, factor de la pata
  (12·db por defecto), preferencia de giro de la pata (hacia el centro de la columna / hacia el eje
  del cimiento / la que tenga más sitio), holgura.

En `Codes/`: método `CompressionDevelopmentLength(ctx)` en `IAnchorageCode`. ACI 318-19 25.4.9 y
E.060 12.3: ldc = 0,24·fy·db/(λ·√f'c) ≥ 0,043·fy·db ≥ 200 mm. Eurocódigo 2: lbd con α1 = 1 (sin gancho).

En `Analysis/`: `FootingAnchorageAnalyzer.Analyze(column, footing, barSets, options)` que devuelve,
por conjunto de barras:

- profundidad disponible bajo la columna (cota superior del cimiento − fondo − recubrimiento) y
  profundidad útil para el exterior de la patilla;
- decisión: recto (compresión o tracción con ld), gancho 90° (ldh) o INSUFICIENTE;
- dirección de la patilla elegida y distancia al borde del cimiento en esa dirección;
- diagnósticos: barra que sale por el fondo, patilla que sale por un lado, patilla sobre la
  parrilla, cimiento no encontrado bajo la columna, columna que apoya sobre dos cimientos.

En `Verification/`: `FootingBarFixer.Plan(...)`, calcado de `BarFixer`: conserva la barra desde
arriba, la corta en `fondo + recubrimiento + db/2` y añade la patilla a 90° en la dirección elegida.
Si la barra original ya tenía patilla pero hacia fuera, la nueva la lleva girada.

Pruebas (xUnit): zapata cuadrada con columna centrada; cimiento corrido de 500 con columna en el
borde (patilla girada); columna en esquina en L (dos direcciones con poco sitio); cimiento demasiado
delgado (INSUFICIENTE); barra que ya cumple (no se toca); barra en compresión con ldc.

### 3.2 Adaptador Revit (`UnionesAcero.Revit`)

`Services/FootingExtractor`:

- Busca elementos de la categoría *Cimentación estructural* (`OST_StructuralFoundation`) cuyo
  sólido corta una recta vertical por el eje de la columna, prolongada hacia abajo desde su base.
  Hay que cubrir los tres modos en que se modela un cimiento corrido: `WallFoundation` (cimiento
  alojado en un muro), losa de cimentación (`Floor` con categoría de cimentación, contorno
  irregular como en la planta de la garita) y familia cargada o in situ (`FamilyInstance`).
- Cota superior e inferior bajo cada barra con `Solid.IntersectWithCurve` sobre una recta vertical
  por la posición de la barra (vale para cimientos escalonados).
- Sección en planta a la cota de la patilla: contorno de la cara horizontal superior del sólido,
  como se hace con la columna. Si la columna apoya en la unión de dos cimientos, unir contornos.
- Sobrecimiento: se lee aparte y **no cuenta** como profundidad de anclaje salvo que el usuario lo
  marque (opción); se avisa cuando la barra termina dentro del sobrecimiento.

`Services/ColumnBarInspector` (ampliar `InferColumnBars`): por cada `Rebar` vertical de la columna,
el eje con y sin ganchos de **cada posición** (`GetCenterlineCurves(..., i)`), para saber dónde
está cada barra en planta y hacia dónde apunta su patilla.

`RebarBuilder.CreateFixed` ya sirve: la normal del plano pasa a ser horizontal (dirección de la
patilla × vertical) y el gancho se pone como terminación al final. Se mantiene la comprobación
posterior de que el gancho queda dentro del sólido (ahora del cimiento) y la inversión si no.

### 3.3 Ventana

En `JointWindow`, cuando bajo la columna hay cimiento:

- La línea de la columna añade "cimiento: corrido 500 x 800, 2 conjuntos a corregir".
- Nuevo grupo **Cimiento bajo la columna**: barras en tracción/compresión, recubrimiento al
  terreno (70), factor de pata (12·db), hacia dónde girar la pata, contar el sobrecimiento.
- Nueva tabla "Barras de la columna": un conjunto por fila con su veredicto (cumple / corregir:
  cortar 120 mm y girar pata / INSUFICIENTE).
- Tercer esquema: **sección vertical por la columna**: cimiento, sobrecimiento, columna, cada
  conjunto con su patilla (a trazos la existente, naranja la corregida), cotas de anclaje provisto y
  requerido y el recubrimiento al fondo. En la planta del nudo, se dibuja el contorno del cimiento
  en gris claro bajo la columna y las patillas como flechas.
- El botón Armar ejecuta a la vez las correcciones de vigas y las de columna.

Alternativa: botón aparte **Cimientos** en el desplegable Acero de la pestaña ARBA, con su propia
ventana. Es más simple de explicar al usuario, pero duplica lista de columnas y opciones. Se
recomienda integrarlo en la misma ventana y activar el grupo solo cuando se encuentra cimiento.

## 4. Orden de trabajo y estimación

| Paso | Qué | Estimación |
|---|---|---|
| 1 | Núcleo: `FootingSection`, `ColumnBarSet`, `FootingOptions`, ldc en las normas, `FootingAnchorageAnalyzer` y pruebas | 1 día |
| 2 | `FootingBarFixer` y pruebas | ½ día |
| 3 | Revit: `FootingExtractor` para `WallFoundation`, losa de cimentación y familia; prueba con el modelo de la garita | 1 día |
| 4 | Revit: lectura por posición de las barras de columna y sustitución con `CreateFixed` | ½ a 1 día |
| 5 | Ventana: grupo, tabla, sección vertical, resumen e informe; README | 1 día |

Total orientativo: 4 a 5 días de trabajo, con el paso 3 como el de más riesgo (variedad de formas
de modelar el cimiento).

## 5. Decidir antes de empezar

1. **Cómo se modelan los cimientos corridos** en los proyectos de la oficina: cimiento de muro
   (`WallFoundation`), losa de cimentación con contorno irregular, o familia. Determina qué casos
   cubre el paso 3 primero.
2. **Regla de anclaje** que se quiere aplicar: en albañilería confinada (E.070) las columnas de
   confinamiento suelen anclarse con la barra hasta el fondo del cimiento ciclópeo más la pata; en
   pórticos (E.060 cap. 21) la barra está en tracción y se exige ldh. La opción
   "tracción/compresión" cubre ambas, pero el valor por defecto lo fija la oficina.
3. **Recubrimiento al terreno** (70 u 75 mm) y **longitud de pata** (12·db u otro valor de la
   oficina).
4. **Cimiento ciclópeo sin armar** frente a **zapata armada**: solo en el segundo caso hay parrilla
   con la que no interferir.
5. **Hacia dónde girar la pata** cuando hay sitio en varias direcciones: hacia el centro de la
   columna (lo habitual) o hacia el eje del cimiento.
6. Si el **sobrecimiento** cuenta o no como longitud de anclaje.

## 6. Fuera de alcance

- Diseñar la zapata o el cimiento (dimensiones, cuantías, punzonamiento).
- Columnas inclinadas o cimientos con base inclinada.
- Pilotes, encepados y pedestales con pernos.
