using System.Linq;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	partial class HammerMeshTool
	{
		static readonly int MaterialMouseHash = "HammerMaterialMouse".GetHashCode();
		int _materialUndoGroup = -1;

		/// <summary>
		/// s&amp;box's material mouse controls, in every mesh mode: Shift+right-click picks up the
		/// material under the mouse, Ctrl+right-click (or drag) paints the active material on.
		/// Returns true if it used the event.
		/// </summary>
		bool MaterialMouseGUI()
		{
			var e = Event.current;
			var id = GUIUtility.GetControlID( MaterialMouseHash, FocusType.Passive );

			switch ( e.GetTypeForControl( id ) )
			{
				case EventType.MouseDown when e.button == 1 && e.shift && !(e.control || e.command):
					LiftMaterial( e.mousePosition );
					e.Use();
					return true;

				case EventType.MouseDown when e.button == 1 && (e.control || e.command) && GUIUtility.hotControl == 0:
					GUIUtility.hotControl = id;
					Undo.IncrementCurrentGroup();
					_materialUndoGroup = Undo.GetCurrentGroup();
					PaintMaterialAt( e.mousePosition );
					e.Use();
					return true;

				// Alt+right click (released without dragging, which would zoom): the selected face's
				// material and alignment onto the face clicked
				case EventType.MouseUp when e.button == 1 && e.alt && GUIUtility.hotControl == 0:
					if ( CopyTextureTo( e.mousePosition ) ) { e.Use(); return true; }
					break;

				case EventType.MouseDrag when GUIUtility.hotControl == id:
					PaintMaterialAt( e.mousePosition );
					e.Use();
					return true;

				case EventType.MouseUp when GUIUtility.hotControl == id:
					GUIUtility.hotControl = 0;
					if ( _materialUndoGroup >= 0 )
					{
						Undo.SetCurrentGroupName( "Paint Material" );
						Undo.CollapseUndoOperations( _materialUndoGroup );
					}
					_materialUndoGroup = -1;
					e.Use();
					return true;
			}

			return false;
		}

		/// <summary>
		/// Put the active material on the face under the mouse.
		/// </summary>
		public void PaintMaterialAt( Vector2 mouse )
		{
			if ( !MeshPicking.PickFace( mouse, MeshPicking.VisibleMeshes(), out var hit ) )
				return;

			var face = hit.Face;
			var material = HammerMaterials.Get( HammerSettings.ActiveMaterial ) ?? S.Material.Load( HammerMaterials.DefaultKey );
			if ( face.Material?.Name == material?.Name )
				return;

			var component = face.Component;
			Undo.RecordObject( component, "Paint Material" );
			component.Mesh.SetFaceMaterial( face.Handle, material );
			// Keep texel density right for the new texture size
			component.Mesh.ComputeFaceTextureCoordinatesFromParameters( new[] { face.Handle } );
			RebuildNow( new[] { component } );
			HammerViews.RepaintAll();
		}

		/// <summary>
		/// Hammer's Alt+right click: the first selected face's material and texture alignment
		/// copied onto the face under the mouse. A neighbour of the same mesh gets the texture
		/// wrapped round the shared edge, so it runs on without a seam.
		/// </summary>
		public bool CopyTextureTo( Vector2 mouse )
		{
			var source = SelectedFaces.FirstOrDefault();
			if ( !source.IsValid || !MeshPicking.PickFace( mouse, MeshPicking.VisibleMeshes(), out var hit ) )
				return false;

			var target = hit.Face;
			if ( target.Equals( source ) ) return false;

			var to = target.Component;
			Undo.IncrementCurrentGroup();
			Undo.RecordObject( to, "Copy Texture" );

			var mesh = to.Mesh;
			mesh.SetFaceMaterial( target.Handle, source.Component.Mesh.GetFaceMaterial( source.Handle ) );

			// Texture axes are in world space, so they carry over between meshes as they are
			var wrapped = to == source.Component && mesh.TextureWrapFromFace( source.Handle, target.Handle );
			if ( !wrapped )
			{
				source.Component.Mesh.GetFaceTextureParameters( source.Handle, out var u, out var v, out var scale );
				mesh.SetFaceTextureParameters( target.Handle, u, v, scale );
			}

			mesh.ComputeFaceTextureCoordinatesFromParameters( new[] { target.Handle } );
			RebuildNow( new[] { to } );
			Undo.SetCurrentGroupName( "Copy Texture" );
			Undo.IncrementCurrentGroup();
			HammerViews.RepaintAll();
			return true;
		}

		// ── Texture nudges (Alt + arrows / brackets / comma, period) ──

		/// <summary>
		/// Shift the texture on the selected faces by a number of grid steps.
		/// </summary>
		public void NudgeTexture( Vector2 steps ) => ShiftTexture( steps * HammerSettings.GridSize );

		/// <summary>
		/// Move the texture so the selected faces' UVs move by this many texels (Fast Texture Tool).
		/// (The texture offset moves the UVs the same way.)
		/// </summary>
		public void MoveTextureUV( Vector2 texels ) => ShiftTexture( texels );

		public void FlipTexture( bool u )
		{
			using ( Scope( u ? "Flip Texture U" : "Flip Texture V" ) )
			{
				foreach ( var f in SelectedFaces.ToList() )
				{
					var mesh = f.Component.Mesh;
					var scale = mesh.GetTextureScale( f.Handle );
					mesh.SetTextureScale( f.Handle, u ? new S.Vector2( -scale.x, scale.y ) : new S.Vector2( scale.x, -scale.y ) );
				}

				foreach ( var c in Selection.Components )
					c.Mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			Done();
		}
	}
}
