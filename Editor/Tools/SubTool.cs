using System.Collections.Generic;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// A modal tool that takes over the scene view until applied or cancelled, like the s&amp;box
	/// clip, bevel and mirror tools. Enter applies, Esc cancels.
	/// </summary>
	public abstract class SubTool
	{
		public HammerMeshTool Tool { get; internal set; }

		public abstract string Title { get; }

		/// <summary>
		/// One line of help shown in the overlay.
		/// </summary>
		public virtual string Help => "Enter to apply, Esc to cancel.";

		/// <summary>
		/// Hammer's key table for the tool, shown in place of <see cref="Help"/> when set.
		/// </summary>
		public virtual (string Key, string Operation)[] Keys => null;

		public virtual void OnEnable() { }

		public abstract void OnViewGUI( HammerView view );

		/// <summary>
		/// Extra controls for the overlay.
		/// </summary>
		public virtual void OnOverlayGUI() { }

		public abstract void Apply();

		public abstract void Cancel();

		/// <summary>
		/// Called for [ and ] while the tool is active. Return true if handled.
		/// </summary>
		public virtual bool OnBracket( int direction ) => false;

		protected void Close() => Tool.EndSubTool( this );

		// ── Undoing setting changes while the tool is open ──
		// Ctrl+Z inside an open tool first steps back through the changes made to its settings
		// (steps, width, shape, which side to keep...), and only cancels the tool once there are
		// none left. The settings are the tool type's static fields.

		readonly Stack<Dictionary<System.Reflection.FieldInfo, object>> _settingsUndo = new();
		readonly Stack<Dictionary<System.Reflection.FieldInfo, object>> _settingsRedo = new();
		Dictionary<System.Reflection.FieldInfo, object> _pendingSettings;

		/// <summary>
		/// Called after a settings undo or redo put values back: update the preview.
		/// </summary>
		protected virtual void OnSettingsUndone() { }

		Dictionary<System.Reflection.FieldInfo, object> Settings()
		{
			var values = new Dictionary<System.Reflection.FieldInfo, object>();
			foreach ( var f in GetType().GetFields( System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic ) )
			{
				if ( f.IsInitOnly || f.IsLiteral ) continue;
				var t = f.FieldType;
				if ( t.IsPrimitive || t.IsEnum || t == typeof( string ) || t == typeof( Vector2 ) || t == typeof( Vector3 ) )
					values[f] = f.GetValue( null );
			}
			return values;
		}

		static bool Same( Dictionary<System.Reflection.FieldInfo, object> a, Dictionary<System.Reflection.FieldInfo, object> b ) =>
			a.Count == b.Count && System.Linq.Enumerable.All( a, kv => b.TryGetValue( kv.Key, out var v ) && Equals( v, kv.Value ) );

		void Restore( Dictionary<System.Reflection.FieldInfo, object> values )
		{
			foreach ( var kv in values ) kv.Key.SetValue( null, kv.Value );
			OnSettingsUndone();
			HammerViews.RepaintAll();
		}

		/// <summary>
		/// Run something that may change settings (the tool's panel, a key, a drag in a view),
		/// remembering the values before if it did.
		/// </summary>
		internal void TrackSettings( System.Action change )
		{
			var before = Settings();
			change();
			if ( Same( before, Settings() ) ) return;
			_settingsUndo.Push( before );
			_settingsRedo.Clear();
		}

		/// <summary>
		/// The view's GUI: a drag that changes a setting (a bevel's width...) is one step,
		/// from mouse down to mouse up.
		/// </summary>
		internal void ViewGUI( HammerView view )
		{
			var e = Event.current;
			var type = e.type;
			if ( type == EventType.MouseDown ) _pendingSettings = Settings();

			if ( type is EventType.KeyDown or EventType.ScrollWheel ) TrackSettings( () => OnViewGUI( view ) );
			else OnViewGUI( view );

			if ( type == EventType.MouseUp && _pendingSettings != null )
			{
				if ( !Same( _pendingSettings, Settings() ) )
				{
					_settingsUndo.Push( _pendingSettings );
					_settingsRedo.Clear();
				}
				_pendingSettings = null;
			}
		}

		/// <summary>Step back one settings change. False when there are none.</summary>
		internal bool UndoSettings()
		{
			if ( _settingsUndo.Count == 0 ) return false;
			_settingsRedo.Push( Settings() );
			Restore( _settingsUndo.Pop() );
			return true;
		}

		/// <summary>Step forward again. False when there's nothing to redo.</summary>
		internal bool RedoSettings()
		{
			if ( _settingsRedo.Count == 0 ) return false;
			_settingsUndo.Push( Settings() );
			Restore( _settingsRedo.Pop() );
			return true;
		}

		/// <summary>
		/// Copy a polygon mesh, returning the face remap so a face subset can follow along.
		/// </summary>
		protected static S.PolygonMesh Copy( S.PolygonMesh source, out Dictionary<FaceHandle, FaceHandle> faces, out Dictionary<HalfEdgeHandle, HalfEdgeHandle> edges )
		{
			var mesh = new S.PolygonMesh();
			mesh.SetTransform( source.Transform );
			mesh.MergeMesh( source, S.Transform.Zero, out _, out edges, out faces );
			return mesh;
		}
	}

	partial class HammerMeshTool
	{
		SubTool _subTool;

		public SubTool SubTool => _subTool;

		public void BeginSubTool( SubTool tool )
		{
			if ( _subTool != null )
				_subTool.Cancel();

			_subTool = tool;
			tool.Tool = this;
			tool.OnEnable();
			HammerViews.RepaintAll();
		}

		internal void EndSubTool( SubTool tool )
		{
			if ( _subTool == tool )
				_subTool = null;

			HammerViews.RepaintAll();
		}
	}
}
