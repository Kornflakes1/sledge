using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Per-user editor settings, stored in Prefs. Sizes are in s&amp;box units (inches).
	/// </summary>
	public static class HammerSettings
	{
		const string Prefix = "HammerUnity.";

		/// <summary>
		/// Keep settings in memory instead of the user's EditorPrefs (which every Unity project on
		/// the machine shares). Tests and automation turn this on so they can change settings
		/// freely without leaving the user's grid, bevel segments and so on altered.
		/// </summary>
		public static bool Isolated
		{
			get => Prefs.Isolated;
			set => Prefs.Isolated = value;
		}

		static class Prefs
		{
			public static bool Isolated;
			static readonly System.Collections.Generic.Dictionary<string, object> _memory = new();

			static T Get<T>( string key, T fallback, System.Func<string, T, T> real ) =>
				Isolated ? (_memory.TryGetValue( key, out var v ) && v is T t ? t : fallback) : real( key, fallback );

			static bool Has( string key ) => Isolated ? _memory.ContainsKey( key ) : EditorPrefs.HasKey( key );

			static void Set<T>( string key, T value, System.Action<string, T> real, System.Func<T, string> text, System.Func<string, T, T> get )
			{
				var had = Has( key );
				var old = had ? get( key, default ) : default;
				if ( had && Equals( old, value ) ) return;

				// Tool settings are part of the undo history, like Hammer's tool properties
				if ( !SettingsUndo.Applying && !NotUndoable.Contains( key ) )
					SettingsUndo.Record( key, typeof( T ), had ? text( old ) : null, text( value ) );

				if ( Isolated ) _memory[key] = value;
				else real( key, value );
			}

			/// <summary>
			/// Settings that aren't edits (view options, lists kept for convenience).
			/// </summary>
			static readonly System.Collections.Generic.HashSet<string> NotUndoable = new()
			{
				Prefix + "RecentMaterials", Prefix + "BackplaneDistance", Prefix + "ForwardSpeedMax",
			};

			static string F( float v ) => v.ToString( "R", System.Globalization.CultureInfo.InvariantCulture );

			public static float GetFloat( string key, float fallback ) => Get( key, fallback, EditorPrefs.GetFloat );
			public static void SetFloat( string key, float value ) => Set( key, value, EditorPrefs.SetFloat, F, GetFloat );
			public static int GetInt( string key, int fallback ) => Get( key, fallback, EditorPrefs.GetInt );
			public static void SetInt( string key, int value ) => Set( key, value, EditorPrefs.SetInt, v => v.ToString(), GetInt );
			public static bool GetBool( string key, bool fallback ) => Get( key, fallback, EditorPrefs.GetBool );
			public static void SetBool( string key, bool value ) => Set( key, value, EditorPrefs.SetBool, v => v ? "1" : "0", GetBool );
			public static string GetString( string key, string fallback ) => Get( key, fallback, EditorPrefs.GetString );
			public static void SetString( string key, string value ) => Set( key, value, EditorPrefs.SetString, v => v ?? "", GetString );

			/// <summary>
			/// Put a setting back to a value from the undo history (null: never set, its default).
			/// </summary>
			public static void Restore( string key, System.Type type, string value )
			{
				if ( value == null )
				{
					if ( Isolated ) _memory.Remove( key );
					else EditorPrefs.DeleteKey( key );
					return;
				}

				var inv = System.Globalization.CultureInfo.InvariantCulture;
				if ( type == typeof( float ) ) { var v = float.Parse( value, inv ); if ( Isolated ) _memory[key] = v; else EditorPrefs.SetFloat( key, v ); }
				else if ( type == typeof( int ) ) { var v = int.Parse( value, inv ); if ( Isolated ) _memory[key] = v; else EditorPrefs.SetInt( key, v ); }
				else if ( type == typeof( bool ) ) { var v = value == "1"; if ( Isolated ) _memory[key] = v; else EditorPrefs.SetBool( key, v ); }
				else { if ( Isolated ) _memory[key] = value; else EditorPrefs.SetString( key, value ); }
			}

			/// <summary>
			/// The setting's current value as stored text (null: not set).
			/// </summary>
			public static string Current( string key, System.Type type )
			{
				if ( !Has( key ) ) return null;
				if ( type == typeof( float ) ) return F( GetFloat( key, 0 ) );
				if ( type == typeof( int ) ) return GetInt( key, 0 ).ToString();
				if ( type == typeof( bool ) ) return GetBool( key, false ) ? "1" : "0";
				return GetString( key, "" );
			}
		}

		/// <summary>
		/// Called after an undo or redo put settings back.
		/// </summary>
		internal static void AfterSettingsUndo()
		{
			SyncUnityGrid();
			HammerViews.RepaintAll();
		}

		internal static void RestoreSetting( string key, System.Type type, string value ) => Prefs.Restore( key, type, value );
		internal static string CurrentSetting( string key, System.Type type ) => Prefs.Current( key, type );

		static readonly float[] GridSizes = { 0.125f, 0.25f, 0.5f, 1, 2, 4, 8, 16, 32, 64, 128, 256, 512 };

		public static float GridSize
		{
			get => Prefs.GetFloat( Prefix + "GridSize", 8 );
			set
			{
				Prefs.SetFloat( Prefix + "GridSize", Mathf.Clamp( value, GridSizes[0], GridSizes[^1] ) );
				SyncUnityGrid();
			}
		}

		/// <summary>
		/// Snap to the grid: on when both the grid snap toggle and snapping as a whole are on.
		/// </summary>
		public static bool GridSnap
		{
			get => SnapEnabled && GridSnapSetting;
			set => GridSnapSetting = value;
		}

		/// <summary>
		/// The grid snap toggle itself, whatever the master switch says.
		/// </summary>
		public static bool GridSnapSetting
		{
			get => Prefs.GetBool( Prefix + "GridSnap", true );
			set => Prefs.SetBool( Prefix + "GridSnap", value );
		}

		/// <summary>
		/// Hammer's magnet: turns all snapping on or off.
		/// </summary>
		public static bool SnapEnabled
		{
			get => Prefs.GetBool( Prefix + "SnapEnabled", true );
			set => Prefs.SetBool( Prefix + "SnapEnabled", value );
		}

		/// <summary>
		/// While moving, jump to a vertex of other geometry near the mouse.
		/// </summary>
		public static bool VertexSnap
		{
			get => Prefs.GetBool( Prefix + "VertexSnap", false );
			set => Prefs.SetBool( Prefix + "VertexSnap", value );
		}

		/// <summary>
		/// While moving objects, stand them on the surface under the mouse.
		/// </summary>
		public static bool SurfaceSnap
		{
			get => Prefs.GetBool( Prefix + "SurfaceSnap", false );
			set => Prefs.SetBool( Prefix + "SurfaceSnap", value );
		}

		/// <summary>
		/// Move textures with geometry instead of keeping them world aligned.
		/// </summary>
		public static bool TextureLock
		{
			get => Prefs.GetBool( Prefix + "TextureLock", false );
			set => Prefs.SetBool( Prefix + "TextureLock", value );
		}

		/// <summary>
		/// Hammer's Texture Lock Component Manipulations: moving vertices, edges or faces keeps
		/// each face's texture where it was on the face (it stretches with it) instead of the
		/// texture staying put in the world.
		/// </summary>
		public static bool TextureLockComponents
		{
			get => Prefs.GetBool( Prefix + "TextureLockComponents", false );
			set => Prefs.SetBool( Prefix + "TextureLockComponents", value );
		}

		public static bool TextureLockScale
		{
			get => Prefs.GetBool( Prefix + "TextureLockScale", false );
			set => Prefs.SetBool( Prefix + "TextureLockScale", value );
		}

		/// <summary>
		/// Drag-selecting draws a free lasso instead of a box.
		/// </summary>
		public static bool LassoSelect
		{
			get => Prefs.GetBool( Prefix + "LassoSelect", false );
			set => Prefs.SetBool( Prefix + "LassoSelect", value );
		}

		/// <summary>
		/// Select elements hidden behind other geometry.
		/// </summary>
		public static bool SelectionThrough
		{
			get => Prefs.GetBool( Prefix + "SelectionThrough", false );
			set => Prefs.SetBool( Prefix + "SelectionThrough", value );
		}

		/// <summary>
		/// Rotation snap in degrees.
		/// </summary>
		public static float AngleSnap
		{
			get => Prefs.GetFloat( Prefix + "AngleSnap", 15 );
			set => Prefs.SetFloat( Prefix + "AngleSnap", Mathf.Max( 0.1f, value ) );
		}

		/// <summary>
		/// Draw the grid in the views.
		/// </summary>
		public static bool ShowGrid
		{
			get => Prefs.GetBool( Prefix + "ShowGrid", true );
			set => Prefs.SetBool( Prefix + "ShowGrid", value );
		}

		/// <summary>
		/// Draw mesh edges over the shaded 3D view.
		/// </summary>
		public static bool ShowWires
		{
			get => Prefs.GetBool( Prefix + "ShowWires", false );
			set => Prefs.SetBool( Prefix + "ShowWires", value );
		}

		public static bool GlobalSpace
		{
			get => Prefs.GetBool( Prefix + "GlobalSpace", true );
			set => Prefs.SetBool( Prefix + "GlobalSpace", value );
		}

		public static int BevelSegments
		{
			get => Prefs.GetInt( Prefix + "BevelSegments", 1 );
			set => Prefs.SetInt( Prefix + "BevelSegments", Mathf.Clamp( value, 1, 32 ) );
		}

		public static Vector2Int QuadSliceCuts
		{
			get => new( Prefs.GetInt( Prefix + "QuadSliceX", 1 ), Prefs.GetInt( Prefix + "QuadSliceY", 1 ) );
			set
			{
				Prefs.SetInt( Prefix + "QuadSliceX", Mathf.Max( 1, value.x ) );
				Prefs.SetInt( Prefix + "QuadSliceY", Mathf.Max( 1, value.y ) );
			}
		}

		public static string PrimitiveType
		{
			get => Prefs.GetString( Prefix + "PrimitiveType", "Box" );
			set => Prefs.SetString( Prefix + "PrimitiveType", value );
		}

		/// <summary>
		/// The material applied to new primitives and with Apply Material (Shift+T).
		/// </summary>
		public static Material ActiveMaterial
		{
			get
			{
				var guid = Prefs.GetString( Prefix + "ActiveMaterial", "" );
				if ( string.IsNullOrEmpty( guid ) ) return null;
				return AssetDatabase.LoadAssetAtPath<Material>( AssetDatabase.GUIDToAssetPath( guid ) );
			}
			set
			{
				var guid = value != null ? AssetDatabase.AssetPathToGUID( AssetDatabase.GetAssetPath( value ) ) : "";
				Prefs.SetString( Prefix + "ActiveMaterial", guid );

				if ( !string.IsNullOrEmpty( guid ) )
				{
					var recent = RecentMaterialGuids;
					recent.Remove( guid );
					recent.Insert( 0, guid );
					if ( recent.Count > 12 ) recent.RemoveRange( 12, recent.Count - 12 );
					Prefs.SetString( Prefix + "RecentMaterials", string.Join( ";", recent ) );
				}
			}
		}

		static System.Collections.Generic.List<string> RecentMaterialGuids =>
			new( Prefs.GetString( Prefix + "RecentMaterials", "" ).Split( new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries ) );

		/// <summary>
		/// Recently used materials, newest first (the material palette).
		/// </summary>
		public static System.Collections.Generic.List<Material> RecentMaterials
		{
			get
			{
				var list = new System.Collections.Generic.List<Material>();
				foreach ( var guid in RecentMaterialGuids )
				{
					var m = AssetDatabase.LoadAssetAtPath<Material>( AssetDatabase.GUIDToAssetPath( guid ) );
					if ( m != null ) list.Add( m );
				}
				return list;
			}
		}

		public static void GridSmaller()
		{
			var size = GridSize;
			for ( int i = GridSizes.Length - 1; i >= 0; i-- )
			{
				if ( GridSizes[i] < size - 0.0001f )
				{
					GridSize = GridSizes[i];
					return;
				}
			}
		}

		public static void GridLarger()
		{
			var size = GridSize;
			foreach ( var s in GridSizes )
			{
				if ( s > size + 0.0001f )
				{
					GridSize = s;
					return;
				}
			}
		}

		/// <summary>
		/// Make Unity's scene grid line up with the Hammer grid.
		/// </summary>
		public static void SyncUnityGrid()
		{
			EditorSnapSettings.gridSize = Vector3.one * (GridSize * SourceSpace.UnitScale);
			HammerViews.RepaintAll();
		}

		/// <summary>
		/// Snap a Unity world position to the grid, axis by axis, in s&amp;box world space.
		/// </summary>
		public static Vector3 SnapWorld( Vector3 world, bool x = true, bool y = true, bool z = true )
		{
			// On a workplane, snap to its grid (the axes are its own)
			if ( Workplane.Active )
			{
				var step = GridSize * SourceSpace.UnitScale;
				var l = Workplane.ToLocal( world );
				if ( x ) l.x = Mathf.Round( l.x / step ) * step;
				if ( y ) l.y = Mathf.Round( l.y / step ) * step;
				if ( z ) l.z = Mathf.Round( l.z / step ) * step;
				return Workplane.ToWorld( l );
			}

			var grid = GridSize;
			var s = SourceSpace.ToSourcePosition( world );
			// Unity X/Y/Z map to s&box -Y/Z/X
			if ( x ) s.y = Mathf.Round( s.y / grid ) * grid;
			if ( y ) s.z = Mathf.Round( s.z / grid ) * grid;
			if ( z ) s.x = Mathf.Round( s.x / grid ) * grid;
			return SourceSpace.ToUnityPosition( s );
		}

		// ── Hammer's per-mode options ──

		static bool Bool( string key, bool fallback ) => Prefs.GetBool( Prefix + key, fallback );
		static float Float( string key, float fallback ) => Prefs.GetFloat( Prefix + key, fallback );

		/// <summary>Vertices > Merge: collapse everything selected into one (true), or only what's within <see cref="MergeDistance"/>.</summary>
		public static bool MergeInfinite { get => Bool( "MergeInfinite", true ); set => Prefs.SetBool( Prefix + "MergeInfinite", value ); }
		public static float MergeDistance { get => Float( "MergeDistance", 0.1f ); set => Prefs.SetFloat( Prefix + "MergeDistance", Mathf.Max( 0.001f, value ) ); }

		// Display
		/// <summary>
		/// Count faces that are merely bent (not flat) as problems. Unity draws them fine as two
		/// triangles, so it's off by default; it matters for Source's map compiler.
		/// </summary>
		public static bool WarnBentFaces
		{
			get => Bool( "WarnBentFaces", false );
			set { Prefs.SetBool( Prefix + "WarnBentFaces", value ); MeshHealth.ForgetAll(); }
		}

		public static bool ShowNormals { get => Bool( "ShowNormals", false ); set => Prefs.SetBool( Prefix + "ShowNormals", value ); }
		public static bool ShowHardSoftEdges { get => Bool( "ShowHardSoftEdges", false ); set => Prefs.SetBool( Prefix + "ShowHardSoftEdges", value ); }
		public static bool DrawWireframe { get => Bool( "DrawWireframe", true ); set => Prefs.SetBool( Prefix + "DrawWireframe", value ); }
		public static bool EdgeLengthPreview { get => Bool( "EdgeLengthPreview", true ); set => Prefs.SetBool( Prefix + "EdgeLengthPreview", value ); }

		// UV Peel
		public static bool PeelAlongV { get => Bool( "PeelAlongV", true ); set => Prefs.SetBool( Prefix + "PeelAlongV", value ); }
		public static bool PeelWorldSpace { get => Bool( "PeelWorldSpace", false ); set => Prefs.SetBool( Prefix + "PeelWorldSpace", value ); }
		public static float PeelURepeats { get => Float( "PeelURepeats", 1 ); set => Prefs.SetFloat( Prefix + "PeelURepeats", value ); }
		public static float PeelVRepeats { get => Float( "PeelVRepeats", 1 ); set => Prefs.SetFloat( Prefix + "PeelVRepeats", value ); }
		public static float PeelUOffset { get => Float( "PeelUOffset", 0 ); set => Prefs.SetFloat( Prefix + "PeelUOffset", value ); }
		public static float PeelVOffset { get => Float( "PeelVOffset", 0 ); set => Prefs.SetFloat( Prefix + "PeelVOffset", value ); }

		// Filtered selection (Faces)
		public static bool FilterMaterial { get => Bool( "FilterMaterial", false ); set => Prefs.SetBool( Prefix + "FilterMaterial", value ); }
		public static bool FilterNormal { get => Bool( "FilterNormal", true ); set => Prefs.SetBool( Prefix + "FilterNormal", value ); }
		public static float FilterNormalAngle { get => Float( "FilterNormalAngle", 15 ); set => Prefs.SetFloat( Prefix + "FilterNormalAngle", Mathf.Clamp( value, 0, 180 ) ); }

		// View 3D (Tools > Hammer > Options)
		/// <summary>How far the 3D views draw, in units.</summary>
		public static float BackplaneDistance { get => Float( "BackplaneDistance", 160000 ); set => Prefs.SetFloat( Prefix + "BackplaneDistance", Mathf.Max( 512, value ) ); }
		/// <summary>The fastest the camera flies, in units per second.</summary>
		public static float ForwardSpeedMax { get => Float( "ForwardSpeedMax", 8192 ); set => Prefs.SetFloat( Prefix + "ForwardSpeedMax", Mathf.Max( 64, value ) ); }

		// Meshes
		public static bool AlignToSurface { get => Bool( "AlignToSurface", false ); set => Prefs.SetBool( Prefix + "AlignToSurface", value ); }
	}
}
