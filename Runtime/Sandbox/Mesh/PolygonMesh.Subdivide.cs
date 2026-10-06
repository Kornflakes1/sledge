using HalfEdgeMesh;

namespace Sandbox;

/// <summary>
/// Smooth subdivision (Catmull-Clark), like Hammer's subdivision levels: every face becomes
/// quads and the surface rounds off. Open edges and hard edges stay sharp. Not part of s&amp;box;
/// added for the Unity port.
/// </summary>
public partial class PolygonMesh
{
	/// <summary>
	/// A smoothed copy, subdivided <paramref name="levels"/> times. The mesh itself is unchanged.
	/// </summary>
	public PolygonMesh Subdivided( int levels )
	{
		var mesh = this;
		for ( int i = 0; i < levels; i++ )
			mesh = mesh.CatmullClark();
		return mesh;
	}

	readonly struct EdgeKey : IEquatable<EdgeKey>
	{
		public readonly int A, B;
		public EdgeKey( int a, int b ) { A = Math.Min( a, b ); B = Math.Max( a, b ); }
		public bool Equals( EdgeKey o ) => A == o.A && B == o.B;
		public override bool Equals( object o ) => o is EdgeKey k && Equals( k );
		public override int GetHashCode() => A * 486187739 ^ B;
	}

	sealed class EdgeInfo
	{
		public int Faces;
		public Vector3 FacePointSum;
		public bool Sharp;
		public int Point = -1;
	}

	PolygonMesh CatmullClark()
	{
		var faces = FaceHandles.ToList();
		var faceCorners = new List<(HalfEdgeHandle Corner, int Vertex)[]>( faces.Count );
		var facePoints = new List<Vector3>( faces.Count );
		var edges = new Dictionary<EdgeKey, EdgeInfo>();

		// Faces: their corners in order, and their middles
		foreach ( var f in faces )
		{
			GetFaceVerticesConnectedToFace( f, out var corners );
			var list = corners.Select( c => (c, GetVertexConnectedToFaceVertex( c ).Index) ).ToArray();
			faceCorners.Add( list );

			var mid = Vector3.Zero;
			foreach ( var (_, v) in list ) mid += GetVertexPosition( VertexHandleFromIndex( v ) );
			facePoints.Add( mid / list.Length );
		}

		// Edges: which faces share them, and whether they stay sharp
		for ( int fi = 0; fi < faces.Count; fi++ )
		{
			var corners = faceCorners[fi];
			for ( int i = 0; i < corners.Length; i++ )
			{
				var key = new EdgeKey( corners[i].Vertex, corners[(i + 1) % corners.Length].Vertex );
				if ( !edges.TryGetValue( key, out var info ) )
				{
					info = new EdgeInfo();
					edges[key] = info;
					var he = FindEdgeConnectingVertices( VertexHandleFromIndex( key.A ), VertexHandleFromIndex( key.B ) );
					info.Sharp = he.IsValid && GetEdgeSmoothing( he ) == EdgeSmoothMode.Hard;
				}
				info.Faces++;
				info.FacePointSum += facePoints[fi];
			}
		}

		var result = new PolygonMesh();
		result.SetTransform( Transform );
		var positions = new List<Vector3>();

		// Edge points
		foreach ( var (key, info) in edges )
		{
			var a = GetVertexPosition( VertexHandleFromIndex( key.A ) );
			var b = GetVertexPosition( VertexHandleFromIndex( key.B ) );
			var crease = info.Faces != 2 || info.Sharp;
			info.Point = positions.Count;
			positions.Add( crease ? (a + b) * 0.5f : (a + b + info.FacePointSum) / 4.0f );
		}

		// Vertex points
		var vertexFaces = new Dictionary<int, (Vector3 Sum, int Count)>();
		for ( int fi = 0; fi < faces.Count; fi++ )
		{
			foreach ( var (_, v) in faceCorners[fi] )
			{
				vertexFaces.TryGetValue( v, out var acc );
				vertexFaces[v] = (acc.Sum + facePoints[fi], acc.Count + 1);
			}
		}

		var vertexEdges = new Dictionary<int, List<(int Other, bool Crease)>>();
		foreach ( var (key, info) in edges )
		{
			var crease = info.Faces != 2 || info.Sharp;
			(vertexEdges.TryGetValue( key.A, out var la ) ? la : vertexEdges[key.A] = new()).Add( (key.B, crease) );
			(vertexEdges.TryGetValue( key.B, out var lb ) ? lb : vertexEdges[key.B] = new()).Add( (key.A, crease) );
		}

		var vertexPoint = new Dictionary<int, int>();
		foreach ( var (v, around) in vertexEdges )
		{
			var p = GetVertexPosition( VertexHandleFromIndex( v ) );
			var creases = around.Where( x => x.Crease ).ToList();
			Vector3 moved;

			if ( creases.Count > 2 )
			{
				moved = p; // a corner where sharp edges meet stays put
			}
			else if ( creases.Count == 2 )
			{
				// Along a sharp edge or border: smooth along it only
				var a = GetVertexPosition( VertexHandleFromIndex( creases[0].Other ) );
				var b = GetVertexPosition( VertexHandleFromIndex( creases[1].Other ) );
				moved = (a + b + p * 6) / 8.0f;
			}
			else
			{
				var n = around.Count;
				var (sum, count) = vertexFaces.TryGetValue( v, out var fc ) ? fc : (Vector3.Zero, 0);
				if ( count == 0 || n < 3 ) { moved = p; }
				else
				{
					var q = sum / count;
					var r = Vector3.Zero;
					foreach ( var (other, _) in around ) r += (p + GetVertexPosition( VertexHandleFromIndex( other ) )) * 0.5f;
					r /= n;
					moved = (q + r * 2 + p * (n - 3)) / n;
				}
			}

			vertexPoint[v] = positions.Count;
			positions.Add( moved );
		}

		// Face points
		var facePointIndex = new int[faces.Count];
		for ( int fi = 0; fi < faces.Count; fi++ )
		{
			facePointIndex[fi] = positions.Count;
			positions.Add( facePoints[fi] );
		}

		var handles = result.AddVertices( positions.ToArray() );

		// Every corner of every face becomes a quad: corner, next edge, middle, previous edge.
		// Texture coordinates follow the same way within the face.
		for ( int fi = 0; fi < faces.Count; fi++ )
		{
			var corners = faceCorners[fi];
			var n = corners.Length;
			var material = GetFaceMaterial( faces[fi] );
			var uv = corners.Select( c => GetTextureCoord( c.Corner ) ).ToArray();
			var uvMid = uv.Aggregate( Vector2.Zero, ( a, b ) => a + b ) / n;

			for ( int i = 0; i < n; i++ )
			{
				var prev = (i + n - 1) % n;
				var next = (i + 1) % n;
				var eNext = edges[new EdgeKey( corners[i].Vertex, corners[next].Vertex )].Point;
				var ePrev = edges[new EdgeKey( corners[prev].Vertex, corners[i].Vertex )].Point;

				var quad = new[] { handles[vertexPoint[corners[i].Vertex]], handles[eNext], handles[facePointIndex[fi]], handles[ePrev] };
				var quadUv = new[] { uv[i], (uv[i] + uv[next]) * 0.5f, uvMid, (uv[prev] + uv[i]) * 0.5f };

				var face = result.AddFace( quad );
				if ( !face.IsValid ) continue;
				if ( material != null ) result.SetFaceMaterial( face, material );

				result.GetFaceVerticesConnectedToFace( face, out var newCorners );
				foreach ( var c in newCorners )
				{
					var vi = Array.IndexOf( quad, result.GetVertexConnectedToFaceVertex( c ) );
					if ( vi >= 0 ) result.SetTextureCoord( c, quadUv[vi] );
				}
			}
		}

		// Hard edges stay hard on the next level too: both halves of each split hard edge
		foreach ( var (key, info) in edges )
		{
			if ( !info.Sharp ) continue;
			foreach ( var end in new[] { key.A, key.B } )
			{
				var he = result.FindEdgeConnectingVertices( handles[vertexPoint[end]], handles[info.Point] );
				if ( he.IsValid ) result.SetEdgeSmoothing( he, EdgeSmoothMode.Hard );
			}
		}

		result.ComputeFaceTextureParametersFromCoordinates();
		result.IsDirty = true;
		return result;
	}
}
