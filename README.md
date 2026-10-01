# Uniones Acero – detallado de nudos viga-columna para Revit 2027

Complemento de Revit (C#, .NET 10, Revit 2027.x) que analiza el encuentro de una **columna de cualquier
sección** (rectangular, L, T, cruz, irregular) con las **vigas que llegan a ella**, decide cómo debe
anclarse cada capa de barras longitudinales (recto, gancho a 90° o barras pasantes), genera esas barras
en el modelo y verifica las que ya estén modeladas.

## Qué hace

Un solo botón, **Nudos**, en el desplegable **Acero** de la pestaña **ARBA** (la misma pestaña y el
mismo estilo que los add-ins de columnas y muros). Seleccionas una o varias columnas y se abre una
**ventana previa**: nada se crea hasta que pulsas *Armar*.

### La ventana

- **Columnas seleccionadas**: forma detectada (rectangular, en L, en T, en cruz…), tamaño, número de
  vigas que llegan y, en rojo, el motivo si no se puede armar. Clic en una fila para ver su nudo.
- **Vigas de la columna seleccionada**: tabla editable con una fila por viga: incluirla o no, tipo de
  barra y número de barras de la capa superior e inferior (vacío / 0 = valores generales), y el
  resultado de cada capa en verde o rojo: *anclaje recto*, *gancho 90°*, *pasante* o *INSUFICIENTE*,
  con la longitud requerida y la provista. Si la viga ya tiene barras modeladas, se proponen las suyas.
- **Normativa y materiales**: ACI 318-19, NSR-10, E.060 o Eurocódigo 2; fy, f'c, nudo sísmico,
  concreto liviano, barras epóxicas.
- **Barras y gancho**: tipo de barra (RebarBarType) y cantidad por capa, tipo de gancho de 90°.
- **Recubrimientos y detallado**: recubrimientos y estribos de columna y viga (si el modelo no los
  define), llevar las barras hasta la cara lejana del núcleo o solo la longitud requerida, extensión
  dentro de la viga, holgura, desplazamiento automático de capas que se cruzan, redondeo.
- **Planta del nudo**: la sección real de la columna con el núcleo a trazos, las vigas que llegan y
  cada barra que se va a crear (superiores en rojo oscuro, inferiores a trazos naranja, pasantes en
  verde azulado, insuficientes en rojo con ✖); el punto al final es el gancho a 90°. Rueda: zoom;
  arrastrar: mover; doble clic: encajar; clic en una viga: su alzado. Al pasar el ratón por una barra
  se ve su anclaje requerido y provisto.
- **Alzado de la viga seleccionada**: corte por el eje de la viga: columna, límite del núcleo, viga,
  cada capa con su gancho (hacia abajo las superiores, hacia arriba las inferiores) y las cotas de
  anclaje provisto y requerido (ld o ldh).
- Las **barras ya modeladas** en las vigas se dibujan a trazos en los dos esquemas: verde si cumplen
  el anclaje, rojo si no; al armar se pueden marcar en rojo en la vista activa.
- **Guardar como valores por defecto** escribe `config.json`; **Armar** crea las barras;
  **Cancelar** no toca nada.

### Al armar

Cada columna se arma en una subtransacción. Las capas con anclaje insuficiente **no se crean** (quedan
en rojo en la ventana para que cambies diámetro, cantidad o columna). Después de crear cada barra con
gancho se lee su geometría real: si el gancho asoma fuera de la columna se invierte la orientación y se
vuelve a crear; si sigue fuera, se descarta esa barra y el informe final lo dice. Las barras se crean
como conjuntos (array) alojados en la viga, con el tipo de barra y de gancho elegidos.

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
5. Si dos vigas se cruzan dentro del nudo con las capas a la misma cota, la segunda capa se desplaza
   automáticamente para que una pase por encima de la otra (configurable).
6. Se avisa si la viga sobresale de la cara de la columna, llega oblicua o no la toca.

## Estructura

```
UnionesAcero.sln
├─ src/UnionesAcero.Core      Núcleo geométrico y normativo. Sin dependencia de Revit (net10.0).
│   ├─ Geometry/              Vec2/Vec3, Segment2D, Polygon2D (recorte de rectas, offset interior…)
│   ├─ Model/                 ColumnSection, BeamSection, Materials, JointOptions
│   ├─ Codes/                 IAnchorageCode: Aci318Code, Nsr10Code, E060Code, Eurocode2Code
│   ├─ Analysis/              JointAnalyzer → JointAnalysis (BeamJoint, LayerResult, BarGroup)
│   ├─ Verification/          ExistingBarChecker (barras ya modeladas)
│   └─ Reporting/             ReportFormatter (informes en texto)
├─ src/UnionesAcero.Revit     Adaptador Revit 2027 (net10.0-windows)
│   ├─ Ribbon/ArbaRibbon.cs   Pestaña ARBA, panel y desplegable Acero (compartidos con los otros add-ins)
│   ├─ Commands/NudosCommand  Selección, análisis, ventana y creación de barras
│   ├─ Services/              Lectura de geometría (columna, vigas, barras existentes), RebarBuilder
│   ├─ UI/                    JointWindow, PlanPreview, ElevationPreview, RevitTheme (WPF en código)
│   └─ config.json            Valores por defecto de la ventana
└─ tests/UnionesAcero.Core.Tests   xUnit (35 pruebas: geometría, normas, analizador, verificador)
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
- Columnas verticales. La sección se toma de la cara horizontal superior del sólido.
- Para armar, el proyecto necesita tipos de barra (`RebarBarType`), un tipo de gancho estándar de 90° y
  formas de armadura cargadas; la ventana no deja armar sin tipo de gancho si hay anclajes con gancho.
- Recubrimientos: se leen del elemento (`RebarHostData`) y si no, de la configuración.
- Barras de las vigas: si la viga ya tiene barras longitudinales modeladas se toman su diámetro y cantidad;
  si no, se usan los valores por defecto de la configuración.

## Limitaciones y cosas a validar en obra/oficina

- Es una herramienta de **detallado geométrico**: no diseña el refuerzo ni verifica cortante en el nudo,
  cuantías ni confinamiento. El ingeniero responsable debe revisar los resultados.
- Las fórmulas están implementadas en MPa con los factores habituales (ψt, ψe, ψg, ψc, λ; α1, α2 en EC2).
  Los casos especiales (barras con cabeza, paquetes de barras, ganchos de 180°) no están cubiertos.
- La orientación del gancho se calcula con la convención `RebarTerminationOrientation` de la API 2026+
  (derecha = dirección × normal) y se comprueba contra el sólido de la columna después de crear la barra:
  si queda fuera se invierte automáticamente.
- La extensión del gancho la fija el tipo de gancho de Revit; si difiere de la que pide la norma
  (12·db a 90° en ACI) el informe lo avisa.
