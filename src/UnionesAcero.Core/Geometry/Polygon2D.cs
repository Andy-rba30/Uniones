namespace UnionesAcero.Core.Geometry;

/// <summary>
/// Polígono simple en planta (sección de columna de cualquier forma: rectangular, L, T, cruz...).
/// Los vértices se almacenan siempre en sentido antihorario (CCW).
/// </summary>
public sealed class Polygon2D
{
    private readonly List<Vec2> _vertices;

    public Polygon2D(IEnumerable<Vec2> vertices)
    {
        _vertices = CleanVertices(vertices);
        if (_vertices.Count < 3)
            throw new ArgumentException("Un polígono necesita al menos 3 vértices no colineales.", nameof(vertices));
        if (SignedArea(_vertices) < 0) _vertices.Reverse();
    }

    public IReadOnlyList<Vec2> Vertices => _vertices;
    public int Count => _vertices.Count;

    public double Area => Math.Abs(SignedArea(_vertices));

    public Vec2 Centroid
    {
        get
        {
            double cx = 0, cy = 0, a = 0;
            for (var i = 0; i < Count; i++)
            {
                var p = _vertices[i];
                var q = _vertices[(i + 1) % Count];
                var cross = p.Cross(q);
                cx += (p.X + q.X) * cross;
                cy += (p.Y + q.Y) * cross;
                a += cross;
            }
            a *= 0.5;
            if (Math.Abs(a) < GeometryUtil.Epsilon) return _vertices[0];
            return new Vec2(cx / (6 * a), cy / (6 * a));
        }
    }

    public (Vec2 Min, Vec2 Max) Bounds
    {
        get
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var v in _vertices)
            {
                minX = Math.Min(minX, v.X); minY = Math.Min(minY, v.Y);
                maxX = Math.Max(maxX, v.X); maxY = Math.Max(maxY, v.Y);
            }
            return (new Vec2(minX, minY), new Vec2(maxX, maxY));
        }
    }

    public Segment2D Edge(int i) => new(_vertices[i], _vertices[(i + 1) % Count]);

    public IEnumerable<Segment2D> Edges()
    {
        for (var i = 0; i < Count; i++) yield return Edge(i);
    }

    /// <summary>Normal exterior unitaria de la arista i (el polígono es CCW).</summary>
    public Vec2 OutwardNormal(int i) => -Edge(i).Direction.Perp();

    /// <summary>¿Es convexo? (útil para decidir estrategias de offset).</summary>
    public bool IsConvex
    {
        get
        {
            for (var i = 0; i < Count; i++)
            {
                var a = _vertices[i];
                var b = _vertices[(i + 1) % Count];
                var c = _vertices[(i + 2) % Count];
                if ((b - a).Cross(c - b) < -GeometryUtil.Epsilon) return false;
            }
            return true;
        }
    }

    /// <summary>Prueba de punto dentro (incluye borde con tolerancia).</summary>
    public bool Contains(Vec2 p, double tol = GeometryUtil.Tolerance)
    {
        if (DistanceToBoundary(p) <= tol) return true;
        var inside = false;
        for (int i = 0, j = Count - 1; i < Count; j = i++)
        {
            var pi = _vertices[i];
            var pj = _vertices[j];
            var crosses = (pi.Y > p.Y) != (pj.Y > p.Y);
            if (!crosses) continue;
            var xInt = (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y) + pi.X;
            if (p.X < xInt) inside = !inside;
        }
        return inside;
    }

    public double DistanceToBoundary(Vec2 p)
    {
        var d = double.MaxValue;
        foreach (var e in Edges()) d = Math.Min(d, e.DistanceTo(p));
        return d;
    }

    /// <summary>Índice de la arista más cercana al punto.</summary>
    public int ClosestEdgeIndex(Vec2 p)
    {
        var best = 0;
        var bd = double.MaxValue;
        for (var i = 0; i < Count; i++)
        {
            var d = Edge(i).DistanceTo(p);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>
    /// Intersecciones de la recta origin + t·dir con el contorno, ordenadas por t.
    /// Cada entrada: parámetro t, índice de arista.
    /// </summary>
    public List<(double T, int EdgeIndex)> LineIntersections(Vec2 origin, Vec2 dir)
    {
        var hits = new List<(double, int)>();
        for (var i = 0; i < Count; i++)
        {
            if (Edge(i).IntersectLine(origin, dir, out var t, out var s))
            {
                // Evitar duplicar un vértice compartido por dos aristas (s≈1 de una y s≈0 de la otra).
                if (s > 1 - 1e-9) continue;
                hits.Add((t, i));
            }
        }
        hits.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return hits;
    }

    /// <summary>
    /// Intervalos [tIn, tOut] de la recta origin + t·dir contenidos en el polígono.
    /// </summary>
    public List<(double TIn, double TOut, int EdgeIn, int EdgeOut)> LineClip(Vec2 origin, Vec2 dir)
    {
        var hits = LineIntersections(origin, dir);
        var result = new List<(double, double, int, int)>();
        for (var i = 0; i + 1 < hits.Count; i += 2)
        {
            var mid = origin + dir * ((hits[i].T + hits[i + 1].T) / 2);
            if (Contains(mid, 1e-3))
                result.Add((hits[i].T, hits[i + 1].T, hits[i].EdgeIndex, hits[i + 1].EdgeIndex));
        }
        return result;
    }

    /// <summary>Longitud del segmento contenida dentro del polígono.</summary>
    public double ClipSegmentLength(Segment2D seg)
    {
        var dir = seg.B - seg.A;
        var len = dir.Length;
        if (len < GeometryUtil.Epsilon) return 0;
        var u = dir / len;
        var total = 0.0;
        foreach (var (tIn, tOut, _, _) in LineClip(seg.A, u))
        {
            var a = Math.Max(tIn, 0);
            var b = Math.Min(tOut, len);
            if (b > a) total += b - a;
        }
        return total;
    }

    /// <summary>
    /// Desde un punto del borde (o interior) y una dirección hacia adentro, profundidad
    /// disponible hasta la primera salida del polígono.
    /// </summary>
    public double RayDepth(Vec2 start, Vec2 inwardDir)
    {
        var u = inwardDir.Normalized();
        var hits = LineIntersections(start, u);
        foreach (var (t, _) in hits)
        {
            if (t > 1e-6)
            {
                var mid = start + u * (t / 2);
                if (Contains(mid, 1e-3)) return t;
            }
        }
        return 0;
    }

    /// <summary>
    /// Offset hacia adentro (contracción) por desplazamiento de aristas e intersección de
    /// rectas adyacentes. Válido para distancias pequeñas respecto al tamaño del polígono
    /// (recubrimientos, estribos), tanto en polígonos convexos como cóncavos (L, T, cruz).
    /// </summary>
    public Polygon2D InwardOffset(double distance)
    {
        if (distance <= 0) return this;
        var pts = new List<Vec2>(Count);
        for (var i = 0; i < Count; i++)
        {
            var prev = Edge((i - 1 + Count) % Count);
            var next = Edge(i);
            var nPrev = -OutwardNormal((i - 1 + Count) % Count);
            var nNext = -OutwardNormal(i);
            var pA = prev.A + nPrev * distance;
            var pB = prev.B + nPrev * distance;
            var qA = next.A + nNext * distance;
            var qB = next.B + nNext * distance;
            var dirP = pB - pA;
            var dirQ = qB - qA;
            var denom = dirP.Cross(dirQ);
            if (Math.Abs(denom) < GeometryUtil.Epsilon)
            {
                pts.Add(pB); // aristas colineales
            }
            else
            {
                var t = (qA - pA).Cross(dirQ) / denom;
                pts.Add(pA + dirP * t);
            }
        }
        return new Polygon2D(pts);
    }

    public static Polygon2D Rectangle(double width, double depth, Vec2? center = null)
    {
        var c = center ?? Vec2.Zero;
        var hw = width / 2; var hd = depth / 2;
        return new Polygon2D(new[]
        {
            new Vec2(c.X - hw, c.Y - hd), new Vec2(c.X + hw, c.Y - hd),
            new Vec2(c.X + hw, c.Y + hd), new Vec2(c.X - hw, c.Y + hd)
        });
    }

    private static double SignedArea(List<Vec2> v)
    {
        double a = 0;
        for (var i = 0; i < v.Count; i++) a += v[i].Cross(v[(i + 1) % v.Count]);
        return a / 2;
    }

    private static List<Vec2> CleanVertices(IEnumerable<Vec2> input)
    {
        var raw = input.ToList();
        if (raw.Count > 1 && raw[0].DistanceTo(raw[^1]) < GeometryUtil.Tolerance) raw.RemoveAt(raw.Count - 1);
        var cleaned = new List<Vec2>();
        foreach (var p in raw)
        {
            if (cleaned.Count == 0 || cleaned[^1].DistanceTo(p) > GeometryUtil.Tolerance) cleaned.Add(p);
        }
        // Eliminar vértices colineales
        var result = new List<Vec2>();
        for (var i = 0; i < cleaned.Count; i++)
        {
            var a = cleaned[(i - 1 + cleaned.Count) % cleaned.Count];
            var b = cleaned[i];
            var c = cleaned[(i + 1) % cleaned.Count];
            var cross = (b - a).Normalized().Cross((c - b).Normalized());
            if (Math.Abs(cross) > 1e-6) result.Add(b);
        }
        return result;
    }

    public override string ToString() => "Polígono[" + string.Join(", ", _vertices) + "]";
}
