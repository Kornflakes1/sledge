using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Hammer's Meshes mode alignment keys that work from the cursor, the last selected object or
	/// the workplane.
	/// </summary>
	public partial class HammerMeshTool
	{
		/// <summary>
		/// Set Origin To Target (Alt+O): each selected object's origin moves to the surface under
		/// the cursor, without moving its geometry.
		/// </summary>
		public void SetOriginToTarget()
		{
			var meshes = SelectedObjectMeshes().ToList();
			if ( meshes.Count == 0 || !SurfaceUnderMouse( meshes, out var hit ) ) return;

			Undo.RecordObjects( meshes.SelectMany( c => new Object[] { c, c.transform } ).ToArray(), "Set Origin To Target" );
			foreach ( var c in meshes )
			{
				MoveOrigin( c, hit.Point );
				c.Commit();
				EditorUtility.SetDirty( c );
			}
			SetObjectPivot( null );
			Done();
		}

		/// <summary>
		/// Pin To Target (Alt+X): move the selection so its pivot sits on the surface under the
		/// cursor, without turning it.
		/// </summary>
		public void PinToTarget()
		{
			var meshes = SelectedObjectMeshes().ToList();
			if ( meshes.Count == 0 || !SurfaceUnderMouse( meshes, out var hit ) ) return;

			var transforms = meshes.Select( c => c.transform ).ToArray();
			Undo.RecordObjects( transforms, "Pin To Target" );
			var delta = hit.Point - ObjectPivot();
			foreach ( var t in transforms ) t.position += delta;
			if ( _objectPivot.HasValue ) SetObjectPivot( _objectPivot.Value + delta );
			Done();
		}

		/// <summary>
		/// The object selected last, and the rest of the selection that follows it.
		/// </summary>
		static bool LastSelected( out Transform last, out Transform[] others )
		{
			last = UnityEditor.Selection.activeTransform;
			var l = last;
			others = UnityEditor.Selection.transforms.Where( t => t != l ).ToArray();
			return last != null && others.Length > 0;
		}

		/// <summary>Snap Position To Last Selected (B): the others move to where it is.</summary>
		public void SnapToLastSelected()
		{
			if ( !LastSelected( out var last, out var others ) ) return;
			Undo.RecordObjects( others, "Snap To Last Selected" );
			foreach ( var t in others ) t.position = last.position;
			Done();
		}

		/// <summary>Align To Last Selected (Alt+B): the others take its position and rotation.</summary>
		public void AlignToLastSelected()
		{
			if ( !LastSelected( out var last, out var others ) ) return;
			Undo.RecordObjects( others, "Align To Last Selected" );
			foreach ( var t in others ) t.SetPositionAndRotation( last.position, last.rotation );
			Done();
		}

		/// <summary>
		/// Align Selected Objects To Workplane (Alt+E): turned to the workplane's axes and set down on it
		/// (the ground, with no workplane).
		/// </summary>
		public void AlignSelectedToWorkplane()
		{
			var transforms = UnityEditor.Selection.transforms;
			if ( transforms.Length == 0 ) return;
			Undo.RecordObjects( transforms, "Align To Workplane" );

			var plane = new Plane( Workplane.Up, Workplane.Active ? Workplane.Origin : Vector3.zero );
			foreach ( var t in transforms )
			{
				t.rotation = Workplane.Rotation;
				t.position = plane.ClosestPointOnPlane( t.position );
			}
			Done();
		}

		/// <summary>Align Workplane To Selected Object (Alt+Q): the workplane takes its position and axes.</summary>
		public void AlignWorkplaneToSelected()
		{
			var t = UnityEditor.Selection.activeTransform;
			if ( t == null ) return;
			Workplane.Set( t.position, t.rotation );
			HammerViews.RepaintAll();
		}
	}
}
