namespace Sandbox;

/// <summary>
/// Replacements for the native mesh helpers s&amp;box calls into.
/// </summary>
public static partial class Mesh
{
	/// <summary>
	/// Triangulate a planar (possibly concave) polygon by ear clipping. Returns indices into
	/// <paramref name="vertices"/>, three per triangle, wound the same way as the polygon.
	/// </summary>
	public static Span<int> TriangulatePolygon( Span<Vector3> vertices )
	{
		int n = vertices.Length;
		if ( n < 3 )
			return Span<int>.Empty;

		if ( n == 3 )
			return new[] { 0, 1, 2 };

		// Newell normal - robust for concave and slightly non-planar polygons
		var normal = Vector3.Zero;
		for ( int i = 0; i < n; i++ )
		{
			var a = vertices[i];
			var b = vertices[(i + 1) % n];
			normal.x += (a.y - b.y) * (a.z + b.z);
			normal.y += (a.z - b.z) * (a.x + b.x);
			normal.z += (a.x - b.x) * (a.y + b.y);
		}

		if ( normal.LengthSquared < 1e-12f )
			return Span<int>.Empty;

		normal = normal.Normal;

		// Project onto the dominant plane
		var axisU = MathF.Abs( normal.z ) > 0.9f ? Vector3.Cross( normal, new Vector3( 1, 0, 0 ) ) : Vector3.Cross( normal, new Vector3( 0, 0, 1 ) );
		axisU = axisU.Normal;
		var axisV = Vector3.Cross( normal, axisU );

		var pts = new Vector2[n];
		for ( int i = 0; i < n; i++ )
			pts[i] = new Vector2( Vector3.Dot( vertices[i], axisU ), Vector3.Dot( vertices[i], axisV ) );

		// The projected polygon is counter-clockwise in (U,V) because V = N x U
		var remaining = new List<int>( n );
		for ( int i = 0; i < n; i++ ) remaining.Add( i );

		var result = new List<int>( (n - 2) * 3 );
		int guard = n * n;

		while ( remaining.Count > 3 && guard-- > 0 )
		{
			bool clipped = false;

			for ( int i = 0; i < remaining.Count; i++ )
			{
				int ip = remaining[(i + remaining.Count - 1) % remaining.Count];
				int ic = remaining[i];
				int inx = remaining[(i + 1) % remaining.Count];

				if ( Cross( pts[ip], pts[ic], pts[inx] ) <= 1e-9f )
					continue; // reflex or degenerate

				bool contains = false;
				for ( int j = 0; j < remaining.Count; j++ )
				{
					int k = remaining[j];
					if ( k == ip || k == ic || k == inx ) continue;
					if ( PointInTriangle( pts[k], pts[ip], pts[ic], pts[inx] ) ) { contains = true; break; }
				}

				if ( contains )
					continue;

				result.Add( ip );
				result.Add( ic );
				result.Add( inx );
				remaining.RemoveAt( i );
				clipped = true;
				break;
			}

			if ( !clipped )
			{
				// Degenerate input - fall back to a fan over what's left
				for ( int i = 1; i < remaining.Count - 1; i++ )
				{
					result.Add( remaining[0] );
					result.Add( remaining[i] );
					result.Add( remaining[i + 1] );
				}

				remaining.Clear();
				break;
			}
		}

		if ( remaining.Count == 3 )
		{
			result.Add( remaining[0] );
			result.Add( remaining[1] );
			result.Add( remaining[2] );
		}

		return result.ToArray();
	}

	static float Cross( Vector2 a, Vector2 b, Vector2 c ) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

	static bool PointInTriangle( Vector2 p, Vector2 a, Vector2 b, Vector2 c )
	{
		return Cross( a, b, p ) >= 0 && Cross( b, c, p ) >= 0 && Cross( c, a, p ) >= 0;
	}
}

partial class Mesh
{
	/// <summary>
	/// Clip the segment <paramref name="start"/> → <paramref name="end"/> against a planar convex or
	/// concave polygon, returning the inside portions as pairs of points (start, end, start, end ...).
	/// Replaces the native s&amp;box helper of the same name.
	/// </summary>
	public static void ClipPolygon( IReadOnlyList<Vector3> polygon, Vector3 start, Vector3 end, out Vector3[] insideSegmentPoints )
	{
		insideSegmentPoints = Array.Empty<Vector3>();

		int n = polygon.Count;
		if ( n < 3 )
			return;

		var normal = Vector3.Zero;
		for ( int i = 0; i < n; i++ )
		{
			var a = polygon[i];
			var b = polygon[(i + 1) % n];
			normal.x += (a.y - b.y) * (a.z + b.z);
			normal.y += (a.z - b.z) * (a.x + b.x);
			normal.z += (a.x - b.x) * (a.y + b.y);
		}

		if ( normal.LengthSquared < 1e-12f )
			return;

		normal = normal.Normal;
		var axisU = (MathF.Abs( normal.z ) > 0.9f ? Vector3.Cross( normal, new Vector3( 1, 0, 0 ) ) : Vector3.Cross( normal, new Vector3( 0, 0, 1 ) )).Normal;
		var axisV = Vector3.Cross( normal, axisU );

		Vector2 Project( Vector3 p ) => new( Vector3.Dot( p, axisU ), Vector3.Dot( p, axisV ) );

		var s = Project( start );
		var e = Project( end );
		var d = e - s;

		// Parameters along the segment where it crosses polygon edges
		var ts = new List<float> { 0.0f, 1.0f };
		for ( int i = 0; i < n; i++ )
		{
			var a = Project( polygon[i] );
			var b = Project( polygon[(i + 1) % n] );
			var edge = b - a;
			var denom = d.x * edge.y - d.y * edge.x;
			if ( MathF.Abs( denom ) < 1e-12f )
				continue;

			var w = a - s;
			var t = (w.x * edge.y - w.y * edge.x) / denom;
			var u = (w.x * d.y - w.y * d.x) / denom;

			if ( t > 0.0f && t < 1.0f && u >= 0.0f && u <= 1.0f )
				ts.Add( t );
		}

		ts.Sort();

		var poly2 = new Vector2[n];
		for ( int i = 0; i < n; i++ ) poly2[i] = Project( polygon[i] );

		var result = new List<Vector3>();
		for ( int i = 0; i < ts.Count - 1; i++ )
		{
			var t0 = ts[i];
			var t1 = ts[i + 1];
			if ( t1 - t0 < 1e-6f )
				continue;

			var mid = s + d * ((t0 + t1) * 0.5f);
			if ( !PointInPolygon( mid, poly2 ) )
				continue;

			var p0 = Vector3.Lerp( start, end, t0, false );
			var p1 = Vector3.Lerp( start, end, t1, false );

			// Merge with the previous segment if contiguous
			if ( result.Count > 0 && result[result.Count - 1].AlmostEqual( p0 ) )
				result[result.Count - 1] = p1;
			else
			{
				result.Add( p0 );
				result.Add( p1 );
			}
		}

		insideSegmentPoints = result.ToArray();
	}

	static bool PointInPolygon( Vector2 p, Vector2[] poly )
	{
		bool inside = false;
		for ( int i = 0, j = poly.Length - 1; i < poly.Length; j = i++ )
		{
			if ( (poly[i].y > p.y) != (poly[j].y > p.y) &&
				p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x )
				inside = !inside;
		}

		return inside;
	}
}
