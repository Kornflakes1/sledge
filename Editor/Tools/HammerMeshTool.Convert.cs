using System.Collections.Generic;
using System.Linq;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// What happens to the selection when switching element mode with a modifier held, like
	/// Hammer's mode buttons: Shift = Boundary, Ctrl = Connect, Alt = Convert.
	/// </summary>
	public enum SelectionConversion
	{
		None,
		/// <summary>Elements on the outside edge of the selection.</summary>
		Boundary,
		/// <summary>Elements connecting the selection (both ends / all corners selected).</summary>
		Connect,
		/// <summary>Every element touching the selection.</summary>
		Convert,
	}

	partial class HammerMeshTool
	{
		/// <summary>
		/// Switch element mode, converting the selection the given way.
		/// </summary>
		public void SwitchMode( EditMode mode, SelectionConversion conversion )
		{
			if ( conversion is SelectionConversion.None or SelectionConversion.Connect || mode is not (EditMode.Vertex or EditMode.Edge or EditMode.Face) || Selection.Count == 0 )
			{
				// Plain switching already keeps the elements the selection connects
				Mode = mode;
				return;
			}

			var elements = Selection.Where( x => x.IsValid ).ToList();
			var result = conversion == SelectionConversion.Boundary ? Boundary( elements, mode ) : Touching( elements, mode );

			_skipConvert = true;
			try { Mode = mode; }
			finally { _skipConvert = false; }

			Selection.Clear();
			foreach ( var element in result )
				Selection.Add( element );

			HammerViews.RepaintAll();
		}

		bool _skipConvert;

		static IEnumerable<IMeshElement> Touching( List<IMeshElement> elements, EditMode to )
		{
			var result = new HashSet<IMeshElement>();

			foreach ( var element in elements )
			{
				var component = element.Component;
				var mesh = component.Mesh;

				switch ( to, element )
				{
					case (EditMode.Vertex, MeshVertex v ): result.Add( v ); break;
					case (EditMode.Vertex, MeshEdge e ):
						mesh.GetEdgeVertices( e.Handle, out var a, out var b );
						result.Add( new MeshVertex( component, a ) );
						result.Add( new MeshVertex( component, b ) );
						break;
					case (EditMode.Vertex, MeshFace f ):
						foreach ( var v in mesh.GetFaceVertices( f.Handle ) ) result.Add( new MeshVertex( component, v ) );
						break;

					case (EditMode.Edge, MeshVertex v ):
						mesh.GetEdgesConnectedToVertex( v.Handle, out var edges );
						foreach ( var he in edges ) result.Add( new MeshEdge( component, he ) );
						break;
					case (EditMode.Edge, MeshEdge e ): result.Add( e ); break;
					case (EditMode.Edge, MeshFace f ):
						foreach ( var he in mesh.GetFaceEdges( f.Handle ) ) result.Add( new MeshEdge( component, he ) );
						break;

					case (EditMode.Face, MeshVertex v ):
						mesh.GetFacesConnectedToVertex( v.Handle, out var faces );
						foreach ( var face in faces ) AddFace( result, component, face );
						break;
					case (EditMode.Face, MeshEdge e ):
						mesh.GetFacesConnectedToEdge( e.Handle, out var fa, out var fb );
						AddFace( result, component, fa );
						AddFace( result, component, fb );
						break;
					case (EditMode.Face, MeshFace f ): result.Add( f ); break;
				}
			}

			return result;
		}

		static void AddFace( HashSet<IMeshElement> set, HammerMesh component, HalfEdgeMesh.FaceHandle face )
		{
			if ( face.IsValid && !component.Mesh.IsFaceHidden( face ) )
				set.Add( new MeshFace( component, face ) );
		}

		static IEnumerable<IMeshElement> Boundary( List<IMeshElement> elements, EditMode to )
		{
			var faces = elements.OfType<MeshFace>().ToHashSet();
			var edges = elements.OfType<MeshEdge>().ToHashSet();
			var result = new HashSet<IMeshElement>();

			if ( faces.Count > 0 && to is EditMode.Vertex or EditMode.Edge )
			{
				// Edges of the selected faces whose face on the other side isn't selected
				foreach ( var face in faces )
				{
					var component = face.Component;
					var mesh = component.Mesh;

					foreach ( var he in mesh.GetFaceEdges( face.Handle ) )
					{
						mesh.GetFacesConnectedToEdge( he, out var a, out var b );
						var other = a.Index == face.Index ? b : a;
						if ( other.IsValid && faces.Contains( new MeshFace( component, other ) ) ) continue;

						var edge = new MeshEdge( component, he );
						if ( to == EditMode.Edge )
						{
							result.Add( edge );
						}
						else
						{
							mesh.GetEdgeVertices( he, out var va, out var vb );
							result.Add( new MeshVertex( component, va ) );
							result.Add( new MeshVertex( component, vb ) );
						}
					}
				}

				return result;
			}

			if ( edges.Count > 0 && to == EditMode.Vertex )
			{
				// Ends of the selected edge chains (all vertices if the chains are closed)
				var uses = new Dictionary<MeshVertex, int>();
				foreach ( var edge in edges )
				{
					edge.Component.Mesh.GetEdgeVertices( edge.Handle, out var a, out var b );
					foreach ( var v in new[] { new MeshVertex( edge.Component, a ), new MeshVertex( edge.Component, b ) } )
						uses[v] = uses.TryGetValue( v, out var n ) ? n + 1 : 1;
				}

				var ends = uses.Where( x => x.Value == 1 ).Select( x => (IMeshElement)x.Key ).ToList();
				return ends.Count > 0 ? ends : uses.Keys.Cast<IMeshElement>();
			}

			if ( faces.Count > 0 || edges.Count > 0 )
			{
				// Faces around the outside of the selection that aren't selected themselves
				var touching = Touching( elements, EditMode.Face ).OfType<MeshFace>();
				return touching.Where( x => !faces.Contains( x ) ).Cast<IMeshElement>();
			}

			// Vertices: the touching elements that aren't entirely inside the selection
			var vertices = elements.OfType<MeshVertex>().ToHashSet();
			return Touching( elements, to ).Where( x => !AllCornersSelected( x, vertices ) );
		}

		static bool AllCornersSelected( IMeshElement element, HashSet<MeshVertex> vertices )
		{
			var mesh = element.Component.Mesh;
			switch ( element )
			{
				case MeshEdge e:
					mesh.GetEdgeVertices( e.Handle, out var a, out var b );
					return vertices.Contains( new MeshVertex( e.Component, a ) ) && vertices.Contains( new MeshVertex( e.Component, b ) );
				case MeshFace f:
					return mesh.GetFaceVertices( f.Handle ).All( v => vertices.Contains( new MeshVertex( f.Component, v ) ) );
				default:
					return true;
			}
		}
	}
}
