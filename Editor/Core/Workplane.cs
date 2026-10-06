using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Hammer's workplane (Shift+Q): a temporary "world" laid on a surface, so the grid, snapping,
	/// the gizmo's world axes and the block tool all follow that surface. Its up is the surface's
	/// normal. Inactive, it's the real world.
	/// </summary>
	public static class Workplane
	{
		public static bool Active { get; private set; }
		public static Vector3 Origin { get; private set; }
		public static Quaternion Rotation { get; private set; } = Quaternion.identity;

		public static Vector3 Up => Rotation * Vector3.up;

		public static void Set( Vector3 origin, Quaternion rotation )
		{
			Active = true;
			Origin = origin;
			Rotation = rotation;
			HammerViews.RepaintAll();
		}

		public static void Reset()
		{
			Active = false;
			Origin = Vector3.zero;
			Rotation = Quaternion.identity;
			HammerViews.RepaintAll();
		}

		public static Vector3 ToLocal( Vector3 world ) => Active ? Quaternion.Inverse( Rotation ) * (world - Origin) : world;
		public static Vector3 ToWorld( Vector3 local ) => Active ? Origin + Rotation * local : local;
		public static Vector3 DirectionToLocal( Vector3 world ) => Active ? Quaternion.Inverse( Rotation ) * world : world;
		public static Vector3 DirectionToWorld( Vector3 local ) => Active ? Rotation * local : local;

		/// <summary>
		/// A workplane lying on a face: up along its normal, its first edge as one of the axes, its
		/// origin on the face.
		/// </summary>
		public static void SetFromFace( MeshFace face )
		{
			var mesh = face.Component.Mesh;
			var normal = face.NormalWorld.normalized;
			var corners = mesh.GetFaceVertices( face.Handle );
			var a = face.Component.SourceToWorld( mesh.GetVertexPosition( corners[0] ) );
			var b = face.Component.SourceToWorld( mesh.GetVertexPosition( corners[1] ) );
			var along = Vector3.ProjectOnPlane( b - a, normal );
			if ( along.sqrMagnitude < 1e-8f ) along = Vector3.ProjectOnPlane( Vector3.forward, normal );
			if ( along.sqrMagnitude < 1e-8f ) along = Vector3.ProjectOnPlane( Vector3.right, normal );

			// The origin on the face's own corner, so the workplane grid lines up with its edges
			Set( a, Quaternion.LookRotation( along.normalized, normal ) );
		}
	}
}
