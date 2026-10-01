# Uniones Acero – detallado de nudos viga-columna para Revit 2027

Complemento de Revit (C#, .NET 10, Revit 2027.x) que analiza el encuentro de una **columna de cualquier
sección** (rectangular, L, T, cruz, irregular) con las **vigas que llegan a ella**, decide cómo debe
anclarse cada capa de barras longitudinales (recto, gancho a 90° o barras pasantes), genera esas barras
en el modelo y verifica las que ya estén modeladas.

## Qué hace

| Botón (pestaña *ARBA*, panel *Nudos viga-columna*) | Acción |
|---|---|
| **Analizar nudo** | Seleccionas una columna. El complemento obtiene su sección en planta, busca las vigas que llegan, calcula para cada capa (superior/inferior) la longitud de desarrollo recta `ld` y con gancho `ldh`, la compara con la profundidad útil de la columna en la dirección de la viga y muestra un informe. No modifica el modelo. |
| **Generar armado** | Igual que Analizar, pero además crea en Revit un conjunto de barras (`Rebar`) por viga y capa, con el gancho a 90° cuando hace falta, repartidas en el ancho de la viga y extendidas dentro de la viga para traslapar. |
| **Verificar armado** | Revisa las barras ya modeladas en las vigas que llegan a la columna, mide cuánto entran en la columna y si tienen gancho, y pinta en rojo (vista activa) las que no cumplen y en verde las que sí. |
| **Configuración** | Normativa, materiales, recubrimientos y barras por defecto. Se guarda en `%AppData%\UnionesAcero\settings.json`. |

Normativas incluidas (seleccionable): **ACI 318-19** (por defecto), **NSR-10** (Colombia), **E.060** (Perú)
y **Eurocódigo 2**. Con la opción *nudo sísmico* se usan las expresiones del capítulo sísmico
(ACI 318 cap. 18: `ldh = fy·db/(5.4·λ·√f'c)`, `ld = 2.5·ldh` ó `3.25·ldh`, y `h_col ≥ 20·db` para barras pasantes).

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
├─ src/UnionesAcero.Revit     Adaptador Revit 2027 (net10.0-windows): cinta, comandos, extracción de
│                             geometría, creación de Rebar, ventanas WPF, manifiesto .addin
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

Al compilar en Windows, el proyecto copia automáticamente `UnionesAcero.Revit.dll`, `UnionesAcero.Core.dll`
y `UnionesAcero.addin` en `%AppData%\Autodesk\Revit\Addins\2027\`. Abre Revit y aparecerá el panel
**Nudos viga-columna** en la pestaña **ARBA** (se crea si no existe). Para desactivar la copia: `dotnet build -p:DeployToRevit=false`.

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
- Para **Generar armado** el proyecto necesita tipos de barra (`RebarBarType`), un tipo de gancho estándar
  de 90° y formas de armadura cargadas; si falta algo, el informe lo indica.
- Recubrimientos: se leen del elemento (`RebarHostData`) y si no, de la configuración.
- Barras de las vigas: si la viga ya tiene barras longitudinales modeladas se toman su diámetro y cantidad;
  si no, se usan los valores por defecto de la configuración.

## Limitaciones y cosas a validar en obra/oficina

- Es una herramienta de **detallado geométrico**: no diseña el refuerzo ni verifica cortante en el nudo,
  cuantías ni confinamiento. El ingeniero responsable debe revisar los resultados.
- Las fórmulas están implementadas en MPa con los factores habituales (ψt, ψe, ψg, ψc, λ; α1, α2 en EC2).
  Los casos especiales (barras con cabeza, paquetes de barras, ganchos de 180°) no están cubiertos.
- La orientación del gancho se calcula con la convención `RebarTerminationOrientation` de la API 2026+
  (derecha = dirección × normal). Conviene comprobar visualmente el primer nudo generado y, si el gancho
  sale invertido en tu plantilla, invertir el signo en `RebarBuilder.HookOrientation`.
- La extensión del gancho la fija el tipo de gancho de Revit; si difiere de la que pide la norma
  (12·db a 90° en ACI) el informe lo avisa.
