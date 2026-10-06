using System;
using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Shift+I. Interactive inset: drag left/right to set the amount, [ ] for extra rings,
	/// Enter to apply. Port of the s&amp;box inset tool.
	/// </summary>
	public sealed class InsetTool : SubTool
	{
		sealed class Target
		{
			public HammerMesh Component;
			public S.PolygonMesh Original;
			public List<int> Faces;
			public List<FaceHandle> Result = new();
		}

		static float _amount = 8;
		static int _steps;

		readonly List<Target> _targets = new();
		bool _dragging;
		float _dragStartAmount;
		Vector2 _dragStartMouse;

		public override string Title => "Inset";
		public override string Help => "Drag left/right to set the inset, [ ] for extra rings. Enter applies, Esc cancels.";

		public static void Open( HammerMeshTool tool )
		{
			var inset = new InsetTool();

			foreach ( var g in tool.SelectedFaces.GroupBy( x => x.Component ) )
				inset._targets.Add( new Target { Component = g.Key, Original = g.Key.Mesh, Faces = g.Select( x => x.Index ).ToList() } );

			if ( inset._targets.Count == 0 ) return;

			tool.BeginSubTool( inset );
			inset.UpdateInset();
		}

		public override bool OnBracket( int direction )
		{
			_steps = Math.Clamp( _steps + direction, 0, 16 );
			UpdateInset();
			return true;
		}

		public override void OnOverlayGUI()
		{
			EditorGUI.BeginChangeCheck();
			_amount = EditorGUILayout.FloatField( "Amount", _amount );
			_steps = EditorGUILayout.IntSlider( "Extra Rings", _steps, 0, 16 );
			if ( EditorGUI.EndChangeCheck() )
				UpdateInset();
		}

		protected override void OnSettingsUndone() => UpdateInset();

		void UpdateInset()
		{
			foreach ( var t in _targets )
			{
				if ( t.Component == null ) continue;

				var mesh = Copy( t.Original, out var faceMap, out _ );
				var faces = t.Faces
					.Select( i => t.Original.FaceHandleFromIndex( i ) )
					.Where( faceMap.ContainsKey )
					.Select( f => faceMap[f] )
					.ToArray();

				t.Result.Clear();

				if ( mesh.InsetFaces( faces, _amount, _steps, out var result ) )
				{
					t.Result.AddRange( result.InsetFaces );
					t.Component.SetPreviewMesh( mesh );
				}
				else
				{
					t.Component.SetPreviewMesh( t.Original );
				}
			}

			HammerViews.RepaintAll();
		}

		public override void OnViewGUI( HammerView view )
		{
			var e = Event.current;
			var id = GUIUtility.GetControlID( FocusType.Passive );
			if ( e.type == EventType.Layout )
				HandleUtility.AddDefaultControl( id );

			switch ( e.GetTypeForControl( id ) )
			{
				case EventType.MouseDown when e.button == 0 && !e.alt:
					_dragging = true;
					_dragStartMouse = e.mousePosition;
					_dragStartAmount = _amount;
					GUIUtility.hotControl = id;
					e.Use();
					break;

				case EventType.MouseDrag when _dragging && GUIUtility.hotControl == id:
				{
					var grid = HammerSettings.GridSize;
					var amount = _dragStartAmount + (e.mousePosition.x - _dragStartMouse.x) / 20.0f * grid;
					if ( HammerSettings.GridSnap ^ (e.control || e.command) )
						amount = Mathf.Round( amount / grid ) * grid;

					if ( !Mathf.Approximately( amount, _amount ) )
					{
						_amount = amount;
						UpdateInset();
					}

					e.Use();
					break;
				}

				case EventType.MouseUp when _dragging && GUIUtility.hotControl == id:
					GUIUtility.hotControl = 0;
					_dragging = false;
					e.Use();
					break;
			}

			if ( e.type == EventType.Repaint )
			{
				Handles.BeginGUI();
				GUI.Label( new Rect( e.mousePosition.x + 16, e.mousePosition.y + 8, 200, 20 ), $"Inset {_amount:0.###}  Rings {_steps}", EditorStyles.whiteBoldLabel );
				Handles.EndGUI();
			}

			view.Repaint();
		}

		public override void Apply()
		{
			var results = _targets.Where( t => t.Component != null ).Select( t => (t, t.Component.Mesh) ).ToList();

			foreach ( var t in _targets )
				if ( t.Component != null ) t.Component.SetPreviewMesh( t.Original );

			Undo.RecordObjects( results.Select( x => (UnityEngine.Object)x.t.Component ).ToArray(), "Inset Faces" );
			Tool.Selection.Clear();

			foreach ( var (t, mesh) in results )
			{
				t.Component.SetPreviewMesh( mesh );
				t.Component.Commit();
				EditorUtility.SetDirty( t.Component );

				foreach ( var f in t.Result )
					if ( f.IsValid ) Tool.Selection.Add( new MeshFace( t.Component, f ) );
			}

			Close();
		}

		public override void Cancel()
		{
			foreach ( var t in _targets )
				if ( t.Component != null ) t.Component.SetPreviewMesh( t.Original );

			Close();
		}
	}
}
