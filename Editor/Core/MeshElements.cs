using System;
using HalfEdgeMesh;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// A vertex, edge or face of a <see cref="HammerMesh"/>. Stored by index so it survives the
	/// mesh being reloaded from serialized data (undo, domain reload).
	/// </summary>
	public interface IMeshElement
	{
		HammerMesh Component { get; }
		int Index { get; }
		bool IsValid { get; }
	}

	public readonly struct MeshVertex : IMeshElement, IEquatable<MeshVertex>
	{
		public HammerMesh Component { get; }
		public int Index { get; }

		public MeshVertex( HammerMesh component, VertexHandle handle ) : this( component, handle.Index ) { }

		public MeshVertex( HammerMesh component, int index )
		{
			Component = component;
			Index = index;
		}

		public VertexHandle Handle => Component != null ? Component.Mesh.VertexHandleFromIndex( Index ) : VertexHandle.Invalid;
		public bool IsValid => Component != null && Handle.IsValid;

		public S.Vector3 PositionLocal => Component.Mesh.GetVertexPosition( Handle );
		public Vector3 PositionWorld => Component.SourceToWorld( PositionLocal );

		public bool Equals( MeshVertex other ) => Component == other.Component && Index == other.Index;
		public override bool Equals( object obj ) => obj is MeshVertex other && Equals( other );
		public override int GetHashCode() => HashCode.Combine( Component, 1, Index );
		public override string ToString() => $"{(Component ? Component.name : "?")} Vertex {Index}";
	}

	public readonly struct MeshEdge : IMeshElement, IEquatable<MeshEdge>
	{
		public HammerMesh Component { get; }
		public int Index { get; }

		/// <summary>
		/// Edges are stored by the lower index of their two half-edges so either half selects the same edge.
		/// </summary>
		public MeshEdge( HammerMesh component, HalfEdgeHandle handle )
		{
			Component = component;
			var opposite = component.Mesh.GetOppositeHalfEdge( handle );
			Index = opposite.IsValid ? Math.Min( handle.Index, opposite.Index ) : handle.Index;
		}

		public HalfEdgeHandle Handle => Component != null ? Component.Mesh.HalfEdgeHandleFromIndex( Index ) : HalfEdgeHandle.Invalid;
		public bool IsValid => Component != null && Handle.IsValid;
		public bool IsOpen => IsValid && Component.Mesh.IsEdgeOpen( Handle );

		public S.Line Line => Component.Mesh.GetEdgeLine( Handle );

		public void GetWorldPoints( out Vector3 a, out Vector3 b )
		{
			var line = Line;
			a = Component.SourceToWorld( line.Start );
			b = Component.SourceToWorld( line.End );
		}

		public S.PolygonMesh.EdgeSmoothMode EdgeSmoothing
		{
			get => IsValid ? Component.Mesh.GetEdgeSmoothing( Handle ) : default;
			set { if ( IsValid ) Component.Mesh.SetEdgeSmoothing( Handle, value ); }
		}

		public bool Equals( MeshEdge other ) => Component == other.Component && Index == other.Index;
		public override bool Equals( object obj ) => obj is MeshEdge other && Equals( other );
		public override int GetHashCode() => HashCode.Combine( Component, 2, Index );
		public override string ToString() => $"{(Component ? Component.name : "?")} Edge {Index}";
	}

	public readonly struct MeshFace : IMeshElement, IEquatable<MeshFace>
	{
		public HammerMesh Component { get; }
		public int Index { get; }

		public MeshFace( HammerMesh component, FaceHandle handle ) : this( component, handle.Index ) { }

		public MeshFace( HammerMesh component, int index )
		{
			Component = component;
			Index = index;
		}

		public FaceHandle Handle => Component != null ? Component.Mesh.FaceHandleFromIndex( Index ) : FaceHandle.Invalid;
		public bool IsValid => Component != null && Handle.IsValid;

		public Vector3 CenterWorld => Component.SourceToWorld( Component.Mesh.GetFaceCenter( Handle ) );

		public Vector3 NormalWorld
		{
			get
			{
				Component.Mesh.ComputeFaceNormal( Handle, out var n );
				return Component.SourceDirectionToWorld( n ).normalized;
			}
		}

		public S.Material Material
		{
			get => IsValid ? Component.Mesh.GetFaceMaterial( Handle ) : null;
			set { if ( IsValid ) Component.Mesh.SetFaceMaterial( Handle, value ); }
		}

		public bool Equals( MeshFace other ) => Component == other.Component && Index == other.Index;
		public override bool Equals( object obj ) => obj is MeshFace other && Equals( other );
		public override int GetHashCode() => HashCode.Combine( Component, 3, Index );
		public override string ToString() => $"{(Component ? Component.name : "?")} Face {Index}";
	}
}
