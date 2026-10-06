using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// One viewport the Hammer tool draws into: a Unity scene view, or one of the Hammer
	/// window's 3D / Top / Front / Side views.
	/// </summary>
	public sealed class HammerView
	{
		public EditorWindow Window;
		public Camera Camera;

		/// <summary>
		/// The viewport's rect in the window's GUI space.
		/// </summary>
		public Rect Rect;

		public string Name;

		/// <summary>
		/// Frame these world bounds in this view.
		/// </summary>
		public Action<Bounds> Frame;

		public bool Orthographic => Camera != null && Camera.orthographic;

		public void Repaint() => Window?.Repaint();
	}

	public static class HammerViews
	{
		static readonly List<EditorWindow> _windows = new();

		/// <summary>
		/// The view currently being drawn, or the last one the tool drew into.
		/// </summary>
		public static HammerView Current { get; internal set; }

		public static void Register( EditorWindow window )
		{
			if ( !_windows.Contains( window ) ) _windows.Add( window );
		}

		public static void Unregister( EditorWindow window ) => _windows.Remove( window );

		/// <summary>
		/// Repaint every scene view and Hammer window.
		/// </summary>
		public static void RepaintAll()
		{
			SceneView.RepaintAll();

			_windows.RemoveAll( x => x == null );
			foreach ( var w in _windows )
				w.Repaint();
		}

		public static HammerView FromSceneView( SceneView sceneView ) => new()
		{
			Window = sceneView,
			Camera = sceneView.camera,
			Rect = new Rect( 0, 0, sceneView.position.width, sceneView.position.height ),
			Name = "Scene",
			Frame = b => sceneView.Frame( b, false ),
		};
	}
}
