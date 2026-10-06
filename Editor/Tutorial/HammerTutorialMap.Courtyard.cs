using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using Sandbox.Primitives;
using UnityEngine;
using S = Sandbox;
using VertexHandle = HalfEdgeMesh.VertexHandle;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// An example scene: a walled garden. A tiered fountain on a brick plaza, wavy brick paths
	/// with kerbs, low garden walls with arched panels following the paths, a Roman arch (after the Arco dei
	/// Gavi) over the way in, bare branching trees, lamps, benches and boulders.
	/// </summary>
	public static partial class HammerTutorialMap
	{
		const string Stone = "Dev Reflectivity 60";
		const string StoneLight = "Dev Reflectivity 80";
		const string StoneDark = "Dev Reflectivity 40";
		const string Brick = "Dev Measure Red";
		const string BrickLight = "Dev Measure Orange";
		const string Grass = "Dev Measure Green";
		const string Bark = "Dev Measure Dark";
		const string Water = "Dev Measure Blue";
		const string Lamp = "Dev Measure Yellow";

		const float PlazaRadius = 520;

		/// <summary>
		/// Build the garden into the open scene. Returns the root object.
		/// </summary>
		public static GameObject BuildCourtyard()
		{
			var root = new GameObject( "Example Garden" );
			Undo.RegisterCreatedObjectUndo( root, "Build Example Garden" );

			var tool = ScriptableObject.CreateInstance<HammerMeshTool>();
			var grid = HammerSettings.GridSize;
			var segments = HammerSettings.BevelSegments;
			var selection = UnityEditor.Selection.objects;

			try
			{
				HammerSettings.GridSize = 8;
				var c = new Context { Root = root, Tool = tool, Stations = new List<Station>() };
				c.Frame( "Garden", S.Vector3.Zero, new S.Vector3( 1, 0, 0 ) );
				var random = new System.Random( 11 );

				Block( c, "Grass", new S.Vector3( 800, 0, -8 ), new S.Vector3( 9600, 7200, 16 ), Grass );

				Plaza( c );
				GardenFountain( c );

				// The way in from the front (through the arch), the long way out the back, and a
				// side path
				var front = new GardenPath( new[] { new S.Vector3( -PlazaRadius + 40, 0, 0 ), new S.Vector3( -900, 150, 0 ), new S.Vector3( -1350, -40, 0 ), new S.Vector3( -1800, 90, 0 ), new S.Vector3( -2600, 0, 0 ) }, 168, 1 );
				var back = new GardenPath( new[] { new S.Vector3( PlazaRadius - 40, 0, 0 ), new S.Vector3( 950, -170, 0 ), new S.Vector3( 1500, 90, 0 ), new S.Vector3( 2150, -140, 0 ), new S.Vector3( 2900, 70, 0 ), new S.Vector3( 3900, -40, 0 ) }, 176, 2 );
				var side = new GardenPath( new[] { new S.Vector3( 0, PlazaRadius - 40, 0 ), new S.Vector3( 160, 900, 0 ), new S.Vector3( -120, 1350, 0 ), new S.Vector3( 80, 1900, 0 ), new S.Vector3( 0, 2600, 0 ) }, 140, 3 );

				foreach ( var (path, name) in new[] { (front, "Front Path"), (back, "Back Path"), (side, "Side Path") } )
					PathAndKerbs( c, name, path );

				// Low walls with arched panels and pillars, following the paths
				const float frontGap = 120;
				var archAt = front.IndexNearX( -1500 );
				foreach ( var s in new[] { -1, 1 } )
				{
					GardenWall( c, "Front Wall", front, s * (front.Width / 2 + frontGap), front.IndexNearX( -620 ), archAt );
					GardenWall( c, "Back Wall", back, s * (back.Width / 2 + 220), back.IndexNearX( 760 ), back.Points.Count - 1 );
				}
				CurvedWallBehindFountain( c );

				FrontArch( c, front, archAt, front.Width / 2 + frontGap );

				// Lamps along the paths, alternating sides
				foreach ( var path in new[] { front, back, side } )
				{
					var n = 0;
					for ( var s = 300f; s < path.Length - 100; s += 420 )
					{
						var (p, t, l) = path.At( s );
						Lamppost( c, p + l * ((n++ % 2 == 0 ? 1 : -1) * (path.Width / 2 + 34)) );
					}
				}

				Trees( c, front, back, side, random );
				Boulders( c, front, back, random );

				tool.Selection.Clear();
				tool.Mode = EditMode.Object;
			}
			finally
			{
				HammerSettings.GridSize = grid;
				HammerSettings.BevelSegments = segments;
				UnityEditor.Selection.objects = selection;
				UnityEngine.Object.DestroyImmediate( tool );
			}

			return root;
		}

		// ───────────────────────────── Paths ─────────────────────────────

		/// <summary>
		/// A smooth curve through some points (Catmull-Rom), sampled finely, with its length so
		/// far at each sample. Width wobbles along it so it doesn't look ruled.
		/// </summary>
		sealed class GardenPath
		{
			public readonly List<S.Vector3> Points = new();
			public readonly List<float> Distance = new();
			public readonly float Width;
			readonly float _phase;

			public float Length => Distance[^1];

			public GardenPath( IList<S.Vector3> controls, float width, int seed )
			{
				Width = width;
				_phase = seed * 1.7f;
				for ( int i = 0; i < controls.Count - 1; i++ )
				{
					var p0 = controls[Math.Max( 0, i - 1 )];
					var p1 = controls[i];
					var p2 = controls[i + 1];
					var p3 = controls[Math.Min( controls.Count - 1, i + 2 )];
					const int steps = 14;
					for ( int k = 0; k < steps; k++ )
					{
						var t = k / (float)steps;
						var t2 = t * t;
						var t3 = t2 * t;
						Points.Add( (p1 * 2 + (p2 - p0) * t + (p0 * 2 - p1 * 5 + p2 * 4 - p3) * t2 + (p1 * 3 - p0 - p2 * 3 + p3) * t3) * 0.5f );
					}
				}
				Points.Add( controls[^1] );

				var total = 0f;
				for ( int i = 0; i < Points.Count; i++ )
				{
					if ( i > 0 ) total += (Points[i] - Points[i - 1]).Length;
					Distance.Add( total );
				}
			}

			public S.Vector3 Tangent( int i ) => (Points[Math.Min( Points.Count - 1, i + 1 )] - Points[Math.Max( 0, i - 1 )]).Normal;

			public S.Vector3 Left( int i ) => S.Vector3.Cross( S.Vector3.Up, Tangent( i ) ).Normal;

			public float WidthAt( int i ) => Width * (1 + 0.12f * MathF.Sin( Distance[i] * 0.004f + _phase ) + 0.05f * MathF.Sin( Distance[i] * 0.013f + _phase * 3 ));

			/// <summary>The surface rises and dips a little: old paving.</summary>
			public float HeightAt( int i ) => 2.5f + 1.2f * MathF.Sin( Distance[i] * 0.009f + _phase ) + 0.6f * MathF.Sin( Distance[i] * 0.031f );

			public int IndexNearX( float x )
			{
				var best = 0;
				for ( int i = 1; i < Points.Count; i++ )
					if ( MathF.Abs( Points[i].x - x ) < MathF.Abs( Points[best].x - x ) ) best = i;
				return best;
			}

			/// <summary>Point, tangent and left at a distance along the path.</summary>
			public (S.Vector3 Point, S.Vector3 Tangent, S.Vector3 Left) At( float s )
			{
				var i = 1;
				while ( i < Points.Count - 1 && Distance[i] < s ) i++;
				var f = Math.Clamp( (s - Distance[i - 1]) / Math.Max( 1e-3f, Distance[i] - Distance[i - 1] ), 0, 1 );
				return (S.Vector3.Lerp( Points[i - 1], Points[i], f ), Tangent( i ), Left( i ));
			}
		}

		/// <summary>
		/// The paving (uneven, wobbling in width) and a kerb either side, swept along the path.
		/// </summary>
		static void PathAndKerbs( Context c, string name, GardenPath path )
		{
			var paving = new S.PolygonMesh();
			Sweep( paving, path.Points, i =>
			{
				var w = path.WidthAt( i ) / 2;
				var h = path.HeightAt( i );
				return new[] { (-w, -2f), (w, -2f), (w, h), (-w, h) };
			} );
			Place( c, name, paving, S.Vector3.Zero, Brick );

			var kerbs = new S.PolygonMesh();
			foreach ( var s in new[] { -1, 1 } )
			{
				Sweep( kerbs, path.Points, i =>
				{
					var x = s * (path.WidthAt( i ) / 2 + 5);
					var h = path.HeightAt( i ) + 3;
					return new[] { (x - 5, -2f), (x + 5, -2f), (x + 5, h), (x + 3, h + 2), (x - 3, h + 2), (x - 5, h) };
				} );
			}
			Place( c, name + " Kerbs", kerbs, S.Vector3.Zero, StoneLight );
		}

		/// <summary>
		/// Brick rings round the fountain, a lighter course every third ring, with a kerb.
		/// </summary>
		static void Plaza( Context c )
		{
			const int sectors = 64;
			const float inner = 368, step = 38;
			var rings = (int)((PlazaRadius - inner) / step);

			var mesh = new S.PolygonMesh();
			var rows = new List<VertexHandle[]>();
			for ( int r = 0; r <= rings; r++ )
			{
				var radius = inner + r * step;
				var points = new S.Vector3[sectors];
				for ( int s = 0; s < sectors; s++ )
				{
					var a = s * MathF.PI * 2 / sectors;
					points[s] = new S.Vector3( MathF.Cos( a ) * radius, MathF.Sin( a ) * radius, 3 );
				}
				rows.Add( mesh.AddVertices( points ) );
			}

			var light = new List<HalfEdgeMesh.FaceHandle>();
			for ( int r = 0; r < rings; r++ )
			{
				for ( int s = 0; s < sectors; s++ )
				{
					var n = (s + 1) % sectors;
					var face = mesh.AddFace( rows[r][s], rows[r + 1][s], rows[r + 1][n], rows[r][n] );
					if ( (r % 3 == 2 || (r + s) % 7 == 0) && face.IsValid ) light.Add( face );
				}
			}

			var plaza = Place( c, "Brick Plaza", mesh, S.Vector3.Zero, Brick );
			plaza.Mesh.AssignMaterialToFaces( light, HammerMaterials.Get( LoadMaterial( BrickLight ) ) );
			Refresh( plaza );

			var kerb = new S.PolygonMesh();
			Lathe( kerb, S.Vector3.Zero, new[] { (PlazaRadius - 2, 0f), (PlazaRadius + 14, 0f), (PlazaRadius + 14, 5f), (PlazaRadius + 10, 7f), (PlazaRadius + 2, 7f), (PlazaRadius - 2, 5f) }, 96, closedLoop: true );
			Place( c, "Plaza Kerb", kerb, S.Vector3.Zero, StoneLight );

			// Paving stones round the foot of the fountain, inside the bricks
			var bed = new S.PolygonMesh();
			Lathe( bed, S.Vector3.Zero, new[] { (0f, 0f), (inner, 0f), (inner, 3f), (0f, 3f) }, sectors );
			Place( c, "Fountain Surround", bed, S.Vector3.Zero, StoneDark );
		}

		// ───────────────────────────── Fountain ─────────────────────────────

		/// <summary>
		/// A tiered fountain from turned outlines (each spun round its axis): a moulded basin, a
		/// turned pedestal, two bowls with lips, a finial, water and a falling curtain.
		/// </summary>
		static void GardenFountain( Context c )
		{
			var stone = new S.PolygonMesh();
			var at = S.Vector3.Zero;

			// Basin: a moulded outer wall, a rolled rim, and a floor
			Lathe( stone, at, new[]
			{
				(0f, 3f), (300f, 3f), (300f, 8f), (292f, 14f), (292f, 44f), (302f, 50f), (304f, 60f), (296f, 68f), (282f, 68f), (274f, 62f), (274f, 22f), (0f, 22f),
			}, 48 );

			// Pedestal: a turned column with a foot, a waist and collars
			Lathe( stone, at, new[]
			{
				(0f, 22f), (64f, 22f), (64f, 32f), (54f, 40f), (46f, 46f), (40f, 70f), (32f, 110f), (36f, 122f), (46f, 128f), (46f, 136f), (32f, 142f), (26f, 168f), (30f, 182f), (0f, 182f),
			}, 32 );

			// Lower bowl: curved underneath, a lip, dished inside
			Lathe( stone, at, new[]
			{
				(0f, 176f), (34f, 176f), (70f, 184f), (118f, 198f), (160f, 216f), (176f, 228f), (178f, 238f), (170f, 244f), (160f, 240f), (120f, 228f), (0f, 222f),
			}, 48 );

			// Upper stem and bowl
			Lathe( stone, at, new[] { (0f, 222f), (24f, 222f), (20f, 236f), (14f, 262f), (18f, 280f), (26f, 286f), (26f, 292f), (0f, 292f) }, 24 );
			Lathe( stone, at, new[]
			{
				(0f, 288f), (22f, 288f), (52f, 296f), (84f, 310f), (98f, 320f), (100f, 328f), (92f, 334f), (84f, 330f), (50f, 320f), (0f, 316f),
			}, 32 );

			// Finial: a stem, a collar, a ball and a point
			Lathe( stone, at, new[] { (0f, 316f), (12f, 316f), (8f, 336f), (14f, 342f), (14f, 346f), (0f, 346f) }, 16 );
			var ball = new List<(float, float)> { (0f, 344f) };
			for ( int i = 1; i < 10; i++ )
			{
				var a = -MathF.PI / 2 + i * MathF.PI / 10;
				ball.Add( (MathF.Cos( a ) * 18, 362 + MathF.Sin( a ) * 18) );
			}
			ball.Add( (0f, 380f) );
			Lathe( stone, at, ball, 16 );
			Lathe( stone, at, new[] { (0f, 378f), (5f, 378f), (0f, 404f) }, 12 );

			var fountain = Place( c, "Fountain", stone, S.Vector3.Zero, StoneLight );
			fountain.SmoothingAngle = 50;
			Refresh( fountain );

			// Water: the pools, a curtain falling from the upper bowl and jets into the basin
			var water = new S.PolygonMesh();
			Lathe( water, at, new[] { (0f, 22f), (275f, 22f), (275f, 52f), (0f, 52f) }, 48 );
			Lathe( water, at, new[] { (0f, 222f), (162f, 222f), (162f, 236f), (0f, 236f) }, 48 );
			Lathe( water, at, new[] { (0f, 316f), (86f, 316f), (86f, 326f), (0f, 326f) }, 32 );
			Lathe( water, at, new[] { (100f, 230f), (106f, 230f), (98f, 328f), (95f, 328f) }, 32, closedLoop: true );
			for ( int i = 0; i < 8; i++ )
			{
				var a = i * MathF.PI / 4 + MathF.PI / 8;
				var d = new S.Vector3( MathF.Cos( a ), MathF.Sin( a ), 0 );
				var jet = new List<S.Vector3>();
				for ( int k = 0; k <= 8; k++ )
				{
					var t = k / 8f;
					jet.Add( d * (176 + t * 70) + new S.Vector3( 0, 0, 236 - t * t * 184 ) );
				}
				Sweep( water, jet, _ => Circle( 3.5f, 6 ) );
			}
			var w = Place( c, "Fountain Water", water, S.Vector3.Zero, Water );
			w.SmoothingAngle = 60;
			Refresh( w );

			// A drain grate ring round the foot
			var grate = new S.PolygonMesh();
			Lathe( grate, at, new[] { (300f, 2f), (326f, 2f), (326f, 4f), (300f, 4f) }, 48, closedLoop: true );
			Place( c, "Fountain Grate", grate, S.Vector3.Zero, Bark );
		}

		// ───────────────────────────── Walls ─────────────────────────────

		/// <summary>
		/// A low wall alongside a path, <paramref name="offset"/> to its left (negative: right):
		/// a plinth, panels whose tops arch up between pillars, a coping that follows them, and a
		/// pillar with a ball every few metres.
		/// </summary>
		static void GardenWall( Context c, string name, GardenPath path, float offset, int from, int to )
		{
			if ( to < from ) (from, to) = (to, from);
			var points = new List<S.Vector3>();
			var along = new List<float>();
			for ( int i = from; i <= to; i++ )
			{
				var p = path.Points[i] + path.Left( i ) * offset;
				// (on the inside of a tight bend the offset line doubles back on itself: skip
				// those points rather than fold the wall)
				if ( points.Count > 0 && ((p - points[^1]).Length < 20 || S.Vector3.Dot( (p - points[^1]).Normal, path.Tangent( i ) ) < 0.5f) ) continue;
				along.Add( points.Count == 0 ? 0 : along[^1] + (p - points[^1]).Length );
				points.Add( p );
			}
			if ( points.Count < 2 ) return;
			BuildWall( c, name, points, along );
		}

		static void CurvedWallBehindFountain( Context c )
		{
			var points = new List<S.Vector3>();
			var along = new List<float>();
			const float radius = PlazaRadius + 150;
			for ( int i = 0; i <= 40; i++ )
			{
				var a = MathF.PI * (1.12f + 0.76f * i / 40f);
				var p = new S.Vector3( MathF.Cos( a ) * radius, MathF.Sin( a ) * radius, 0 );
				along.Add( points.Count == 0 ? 0 : along[^1] + (p - points[^1]).Length );
				points.Add( p );
			}
			BuildWall( c, "Fountain Wall", points, along );

			// Benches in front of it, facing the fountain
			for ( int i = 0; i < 3; i++ )
			{
				var a = MathF.PI * (1.3f + 0.2f * i);
				Bench( c, new S.Vector3( MathF.Cos( a ), MathF.Sin( a ), 0 ) * (radius - 70), a + MathF.PI / 2 );
			}
		}

		static void BuildWall( Context c, string name, List<S.Vector3> points, List<float> along )
		{
			const float bay = 220, low = 60, rise = 20, thick = 18;
			float Top( float s ) => low + rise * MathF.Sin( MathF.PI * ((s % bay) / bay) );

			var wall = new S.PolygonMesh();
			Sweep( wall, points, i => new[] { (-thick / 2, 10f), (thick / 2, 10f), (thick / 2, Top( along[i] )), (-thick / 2, Top( along[i] )) } );
			Place( c, name, wall, S.Vector3.Zero, Stone );

			var trim = new S.PolygonMesh();
			Sweep( trim, points, i => new[] { (-thick / 2 - 6, -2f), (thick / 2 + 6, -2f), (thick / 2 + 6, 10f), (thick / 2 + 2, 14f), (-thick / 2 - 2, 14f), (-thick / 2 - 6, 10f) } );
			Sweep( trim, points, i =>
			{
				var t = Top( along[i] );
				return new[] { (-thick / 2 - 3, t), (thick / 2 + 3, t), (thick / 2 + 3, t + 3), (0f, t + 6), (-thick / 2 - 3, t + 3) };
			} );
			Place( c, name + " Coping", trim, S.Vector3.Zero, StoneLight );

			// Pillars where the panels meet
			var pillars = new S.PolygonMesh();
			var total = along[^1];
			for ( var s = 0f; s <= total + 1; s += bay )
			{
				var i = along.FindIndex( x => x >= s - 0.5f );
				if ( i < 0 ) i = along.Count - 1;
				var t = (points[Math.Min( points.Count - 1, i + 1 )] - points[Math.Max( 0, i - 1 )]).Normal;
				Pillar( pillars, points[i], MathF.Atan2( t.y, t.x ), 96 );
			}
			var p = Place( c, name + " Pillars", pillars, S.Vector3.Zero, StoneLight );
			p.SmoothingAngle = 50;
			Refresh( p );
		}

		/// <summary>A square pillar: plinth, shaft, cap, and a ball on a short neck.</summary>
		static void Pillar( S.PolygonMesh m, S.Vector3 at, float yaw, float height )
		{
			Box( m, at + new S.Vector3( 0, 0, 8 ), new S.Vector3( 40, 40, 16 ), yaw );
			Box( m, at + new S.Vector3( 0, 0, 16 + (height - 28) / 2 ), new S.Vector3( 30, 30, height - 28 ), yaw );
			Box( m, at + new S.Vector3( 0, 0, height - 8 ), new S.Vector3( 40, 40, 6 ), yaw );
			Box( m, at + new S.Vector3( 0, 0, height - 2 ), new S.Vector3( 34, 34, 6 ), yaw );
			var ball = new List<(float, float)> { (0f, height + 1), (7f, height + 1), (5f, height + 6) };
			for ( int i = 1; i < 8; i++ )
			{
				var a = -MathF.PI / 2 + i * MathF.PI / 8;
				ball.Add( (MathF.Cos( a ) * 12, height + 18 + MathF.Sin( a ) * 12) );
			}
			ball.Add( (0f, height + 30) );
			Lathe( m, at, ball, 12 );
		}

		/// <summary>
		/// The way in: a Roman arch like the Arco dei Gavi. A deep block with one big arch through
		/// it and smaller arches through the sides, niches cut beside it (Boolean), fluted columns
		/// on tall pedestals, a moulded entablature with dentils, a pediment over the arch on both
		/// faces, and an attic with a low roof.
		/// </summary>
		static void FrontArch( Context c, GardenPath path, int index, float halfSpan )
		{
			var center = path.Points[index];
			var t = path.Tangent( index );
			var l = path.Left( index );
			var yaw = MathF.Atan2( t.y, t.x );
			const float width = 380, depth = 150, body = 250, door = 150, doorHeight = 160, arch = 75;

			// Points across the arch (x: through it, y: along it, z: up)
			S.Vector3 At( float x, float y, float z ) => center + t * x + l * y + new S.Vector3( 0, 0, z );
			S.PolygonMesh Moved( S.PolygonMesh mesh )
			{
				foreach ( var v in mesh.VertexHandles.ToList() )
				{
					var p = mesh.GetVertexPosition( v );
					mesh.SetVertexPosition( v, At( p.x, p.y, p.z ) );
				}
				return mesh;
			}

			// The body: a Doorway shape, then the side arches and niches subtracted from it
			var doorway = new DoorwayPrimitive { DoorWidth = door, DoorHeight = doorHeight, ArchHeight = arch, ArchSegments = 20, AlignToCamera = false };
			var block = Place( c, "Triumphal Arch", Moved( doorway.CreateMesh( new S.BBox( new S.Vector3( -depth / 2, -width / 2, 0 ), new S.Vector3( depth / 2, width / 2, body ) ) ) ), S.Vector3.Zero, Stone );

			var cutters = new List<HammerMesh>();
			// Through the sides: an arch-shaped prism along the block
			var sideArch = new List<(float, float)> { (-36f, -8f), (36f, -8f), (36f, 120f) };
			for ( int i = 1; i < 12; i++ )
			{
				var a = i * MathF.PI / 12;
				sideArch.Add( (MathF.Cos( a ) * 36, 120 + MathF.Sin( a ) * 36) );
			}
			sideArch.Add( (-36f, 120f) );
			var side = new S.PolygonMesh();
			Sweep( side, new[] { At( 0, -width / 2 - 20, 0 ), At( 0, width / 2 + 20, 0 ) }, _ => sideArch );
			cutters.Add( Place( c, "Side Arch Cutter", side, S.Vector3.Zero, Stone ) );

			// Niches between the paired columns, on both faces
			foreach ( var x in new[] { -1, 1 } )
				foreach ( var y in new[] { -1, 1 } )
				{
					var niche = new S.PolygonMesh();
					Box( niche, At( x * depth / 2, y * 135, 120 ), new S.Vector3( 24, 34, 96 ), yaw );
					cutters.Add( Place( c, "Niche Cutter", niche, S.Vector3.Zero, Stone ) );
				}
			c.Tool.Mode = EditMode.Object;
			Boolean( c, "Subtract", S.PolygonMesh.BooleanOperation.Subtract, block, cutters.ToArray() );

			var trim = new S.PolygonMesh();

			// Archivolt: a moulded band round the arch on both faces, down the jambs
			var curve = new List<S.Vector3>();
			curve.Add( new S.Vector3( 0, -door / 2 - 8, 0 ) );
			for ( int i = 0; i <= 20; i++ )
			{
				var a = MathF.PI - i * MathF.PI / 20;
				curve.Add( new S.Vector3( 0, MathF.Cos( a ) * (door / 2 + 8), doorHeight + MathF.Sin( a ) * (arch + 8) ) );
			}
			curve.Add( new S.Vector3( 0, door / 2 + 8, 0 ) );
			foreach ( var x in new[] { -1, 1 } )
			{
				var line = curve.Select( p => At( x * (depth / 2 + 1), p.y, p.z ) ).ToList();
				float x0 = x < 0 ? -7 : -1, x1 = x < 0 ? 1 : 7;
				Sweep( trim, line, _ => new[] { (x0, -6f), (x1, -6f), (x1, 8f), (x0, 8f) }, fixedSide: t );
				// Imposts where the arch springs, and the keystone
				foreach ( var s in new[] { -1, 1 } )
					Box( trim, At( x * (depth / 2 + 3), s * (door / 2 + 10), doorHeight - 4 ), new S.Vector3( 8, 26, 10 ), yaw );
				Box( trim, At( x * (depth / 2 + 5), 0, doorHeight + arch + 2 ), new S.Vector3( 12, 22, 34 ), yaw );
			}

			// Fluted columns on tall pedestals: two either side of the arch, both faces
			foreach ( var x in new[] { -1, 1 } )
			{
				foreach ( var y in new[] { -width / 2 + 24, -door / 2 - 26, door / 2 + 26, width / 2 - 24 } )
				{
					var px = x * (depth / 2 + 16);
					Box( trim, At( px, y, 6 ), new S.Vector3( 48, 48, 12 ), yaw );
					Box( trim, At( px, y, 40 ), new S.Vector3( 40, 40, 56 ), yaw );
					Box( trim, At( px, y, 72 ), new S.Vector3( 46, 46, 8 ), yaw );
					Lathe( trim, At( px, y, 0 ), new[]
					{
						(0f, 76f), (17f, 76f), (17f, 80f), (14f, 86f), (14f, 92f), (12f, 96f), (11f, 222f), (13f, 226f), (13f, 230f), (0f, 230f),
					}, 24, sideScale: k => k % 2 == 0 ? 1f : 0.88f );
					// Capital: a flared bell under a square abacus with clipped corners
					Lathe( trim, At( px, y, 0 ), new[] { (0f, 230f), (12f, 230f), (15f, 236f), (19f, 242f), (20f, 246f), (0f, 246f) }, 12 );
					Box( trim, At( px, y, 249 ), new S.Vector3( 42, 42, 6 ), yaw );
				}
			}

			// Entablature right round the block: architrave, frieze, dentils, cornice
			// (clockwise from above, so "sideways" in the outline points out of the block)
			var ring = new[] { At( -depth / 2, width / 2, 0 ), At( depth / 2, width / 2, 0 ), At( depth / 2, -width / 2, 0 ), At( -depth / 2, -width / 2, 0 ) };
			for ( int i = 0; i < 4; i++ )
			{
				var a = ring[i];
				var b = ring[(i + 1) % 4];
				var dir = (b - a).Normal;
				var line = new[] { a - dir * 24, b + dir * 24 };
				Sweep( trim, line, _ => new[]
				{
					(-44f, 252f), (4f, 252f), (4f, 262f), (8f, 266f), (8f, 268f), (2f, 270f), (2f, 284f), (14f, 288f), (20f, 296f), (24f, 300f), (24f, 306f), (-44f, 306f),
				} );
			}
			foreach ( var x in new[] { -1, 1 } )
				for ( var y = -width / 2 - 16f; y <= width / 2 + 16f; y += 14 )
					Box( trim, At( x * (depth / 2 + 15), y, 286 ), new S.Vector3( 6, 7, 6 ), yaw );

			// Pediments over the arch on both faces: a triangle, raking cornices along its slopes
			foreach ( var x in new[] { -1, 1 } )
			{
				const float half = 120, peak = 64, baseZ = 306;
				var face = x * (depth / 2 + 8);
				var across = x > 0 ? new[] { At( face - 10, 0, 0 ), At( face + 4, 0, 0 ) } : new[] { At( face + 10, 0, 0 ), At( face - 4, 0, 0 ) };
				Sweep( trim, across, _ => new[] { (half, baseZ), (0f, baseZ + peak), (-half, baseZ) } );
				foreach ( var s in new[] { -1, 1 } )
				{
					var slope = new[] { At( face, s * (half + 14), baseZ - 2 ), At( face, 0, baseZ + peak + 8 ) };
					Sweep( trim, slope, _ => new[] { (-10f, -4f), (12f, -4f), (12f, 6f), (-10f, 6f) } );
				}
			}

			// Attic and a low hipped roof
			Box( trim, At( 0, 0, 330 ), new S.Vector3( depth - 10, width - 20, 48 ), yaw );
			Box( trim, At( 0, 0, 356 ), new S.Vector3( depth, width, 6 ), yaw );
			Sweep( trim, new[] { At( 0, -width / 2 + 10, 0 ), At( 0, width / 2 - 10, 0 ) }, _ => new[] { (-depth / 2 + 4, 359f), (depth / 2 - 4, 359f), (0f, 392f) } );

			var t2 = Place( c, "Triumphal Arch Trim", trim, S.Vector3.Zero, StoneLight );
			t2.SmoothingAngle = 50;
			Refresh( t2 );
		}

		static void Lamppost( Context c, S.Vector3 at )
		{
			var post = new S.PolygonMesh();
			Lathe( post, at, new[] { (0f, 0f), (12f, 0f), (12f, 6f), (8f, 12f), (6f, 24f), (4f, 30f), (4f, 118f), (7f, 122f), (7f, 126f), (0f, 126f) }, 8 );
			var p = Place( c, "Lamppost", post, S.Vector3.Zero, Bark );
			p.SmoothingAngle = 60;
			Refresh( p );

			var head = new S.PolygonMesh();
			Lathe( head, at, new[] { (0f, 126f), (7f, 126f), (11f, 136f), (12f, 150f), (7f, 156f), (10f, 158f), (0f, 166f) }, 6 );
			Place( c, "Lamp", head, S.Vector3.Zero, Lamp );
		}

		static void Bench( Context c, S.Vector3 at, float yaw )
		{
			var bench = new S.PolygonMesh();
			var along = new S.Vector3( MathF.Cos( yaw ), MathF.Sin( yaw ), 0 );
			var back = new S.Vector3( -along.y, along.x, 0 );
			for ( int i = 0; i < 3; i++ )
				Box( bench, at + back * (-8 + i * 8) + new S.Vector3( 0, 0, 18 ), new S.Vector3( 96, 6, 2 ), yaw );
			for ( int i = 0; i < 2; i++ )
				Box( bench, at + back * 12 + new S.Vector3( 0, 0, 26 + i * 8 ), new S.Vector3( 96, 2, 5 ), yaw );
			foreach ( var s in new[] { -1, 1 } )
			{
				Box( bench, at + along * (s * 40) + new S.Vector3( 0, 0, 9 ), new S.Vector3( 4, 22, 18 ), yaw );
				Box( bench, at + along * (s * 40) + back * 12 + new S.Vector3( 0, 0, 26 ), new S.Vector3( 4, 3, 18 ), yaw );
			}
			Place( c, "Bench", bench, S.Vector3.Zero, Bark );
		}

		// ───────────────────────────── Trees and rocks ─────────────────────────────

		static void Trees( Context c, GardenPath front, GardenPath back, GardenPath side, System.Random random )
		{
			float Range( float a, float b ) => a + (float)random.NextDouble() * (b - a);

			// Outside the walls along the paths, and in the grass round the plaza
			var spots = new List<S.Vector3>();
			foreach ( var (path, gap) in new[] { (front, 230f), (back, 360f), (side, 150f) } )
			{
				for ( var s = 180f; s < path.Length; s += Range( 260, 380 ) )
				{
					var (p, _, l) = path.At( s );
					var sideSign = random.NextDouble() < 0.5 ? -1 : 1;
					spots.Add( p + l * (sideSign * (path.Width / 2 + gap + Range( 0, 140 ))) );
					if ( random.NextDouble() < 0.6 ) spots.Add( p + l * (-sideSign * (path.Width / 2 + gap + Range( 0, 160 ))) );
				}
			}
			for ( int i = 0; i < 9; i++ )
			{
				var a = i * MathF.PI * 2 / 9 + 0.3f;
				if ( MathF.Abs( MathF.Cos( a ) ) > 0.9f || MathF.Sin( a ) > 0.85f ) continue; // keep the paths clear
				spots.Add( new S.Vector3( MathF.Cos( a ), MathF.Sin( a ), 0 ) * (PlazaRadius + Range( 260, 420 )) );
			}

			foreach ( var spot in spots )
				BareTree( c, spot, Range( 0.85f, 1.3f ), random );
		}

		/// <summary>
		/// A bare tree: a curved, tapering trunk that splits into branches, which split again,
		/// each a tube swept along a bent line. A ring of mulch round the foot.
		/// </summary>
		static void BareTree( Context c, S.Vector3 at, float scale, System.Random random )
		{
			float Range( float a, float b ) => a + (float)random.NextDouble() * (b - a);
			var wood = new S.PolygonMesh();

			void Branch( S.Vector3 from, S.Vector3 direction, float length, float radius, int depth )
			{
				// A bent limb: three points, the middle nudged sideways
				var bend = S.Vector3.Cross( direction, new S.Vector3( Range( -1, 1 ), Range( -1, 1 ), 0.2f ) ).Normal * (length * Range( 0.05f, 0.14f ));
				var mid = from + direction * (length * 0.5f) + bend;
				var end = from + direction * length + bend * 0.4f;
				var tip = radius * 0.62f;
				Sweep( wood, new[] { from - direction * (radius * 0.6f), mid, end }, i => Circle( i == 0 ? radius : i == 1 ? (radius + tip) / 2 : tip, depth == 0 ? 9 : 6 ) );

				if ( depth >= 3 || radius < 2 ) return;
				var children = depth == 0 ? 3 : random.NextDouble() < 0.5 ? 2 : 3;
				var spin = Range( 0, MathF.PI * 2 );
				for ( int k = 0; k < children; k++ )
				{
					var a = spin + k * MathF.PI * 2 / children + Range( -0.4f, 0.4f );
					var outward = new S.Vector3( MathF.Cos( a ), MathF.Sin( a ), 0 );
					var tilt = depth == 0 ? Range( 0.45f, 0.75f ) : Range( 0.5f, 0.95f );
					var dir = (direction * (1 - tilt) + outward * tilt + new S.Vector3( 0, 0, 0.25f )).Normal;
					Branch( end, dir, length * Range( 0.6f, 0.78f ), tip * Range( 0.7f, 0.85f ), depth + 1 );
				}
			}

			var lean = new S.Vector3( Range( -0.12f, 0.12f ), Range( -0.12f, 0.12f ), 1 ).Normal;
			Branch( at + new S.Vector3( 0, 0, -4 ), lean, Range( 120, 170 ) * scale, 13 * scale, 0 );

			var tree = Place( c, "Tree", wood, S.Vector3.Zero, Bark );
			tree.SmoothingAngle = 70;
			Refresh( tree );

			var mulch = new S.PolygonMesh();
			Lathe( mulch, at, new[] { (0f, -1f), (64f * scale, -1f), (64f * scale, 1f), (56f * scale, 3f), (0f, 3f) }, 20 );
			Place( c, "Tree Bed", mulch, S.Vector3.Zero, BrickLight );
		}

		static void Boulders( Context c, GardenPath front, GardenPath back, System.Random random )
		{
			float Range( float a, float b ) => a + (float)random.NextDouble() * (b - a);
			foreach ( var (path, s, sideSign) in new[] { (front, 520f, -1), (front, 1000f, 1), (back, 700f, 1), (back, 1500f, -1), (back, 2300f, 1) } )
			{
				var (p, _, l) = path.At( s );
				var rocks = new S.PolygonMesh();
				var center = p + l * (sideSign * (path.Width / 2 + 90));
				var big = Range( 46, 70 );
				Rock( rocks, center + new S.Vector3( 0, 0, big * 0.25f ), big, random );
				for ( int i = 0; i < 3; i++ )
				{
					var r = big * Range( 0.25f, 0.45f );
					var a = Range( 0, MathF.PI * 2 );
					Rock( rocks, center + new S.Vector3( MathF.Cos( a ), MathF.Sin( a ), 0 ) * (big + r * 0.6f) + new S.Vector3( 0, 0, r * 0.2f ), r, random );
				}
				Place( c, "Boulders", rocks, S.Vector3.Zero, Stone );
			}
		}

		// ───────────────────────────── Mesh building ─────────────────────────────

		static (float, float)[] Circle( float radius, int sides ) =>
			Enumerable.Range( 0, sides ).Select( i => (MathF.Cos( i * MathF.PI * 2 / sides ) * radius, MathF.Sin( i * MathF.PI * 2 / sides ) * radius) ).ToArray();

		/// <summary>
		/// Add a face, wound so it points along <paramref name="outward"/>. A four-sided face
		/// that isn't flat is split in two, so nothing comes out bent.
		/// </summary>
		static void Face( S.PolygonMesh m, VertexHandle[] v, S.Vector3 outward )
		{
			var p = v.Select( m.GetVertexPosition ).ToArray();
			var normal = S.Vector3.Zero;
			for ( int i = 0; i < p.Length; i++ )
			{
				var a = p[i];
				var b = p[(i + 1) % p.Length];
				normal += new S.Vector3( (a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y) );
			}
			if ( normal.Length < 1e-6f ) return;

			if ( p.Length == 4 )
			{
				var n = normal.Normal;
				var mid = (p[0] + p[1] + p[2] + p[3]) * 0.25f;
				if ( p.Any( x => MathF.Abs( S.Vector3.Dot( x - mid, n ) ) > 0.01f ) )
				{
					Face( m, new[] { v[0], v[1], v[2] }, outward );
					Face( m, new[] { v[0], v[2], v[3] }, outward );
					return;
				}
			}

			m.AddFace( S.Vector3.Dot( normal, outward ) >= 0 ? v : v.Reverse().ToArray() );
		}

		/// <summary>
		/// Sweep a closed outline along a line of points: (x, z) is sideways and up across the
		/// line. The outline can change along the way (it must keep its point count). Capped at
		/// both ends. <paramref name="fixedSide"/> keeps "sideways" one direction (for arches).
		/// </summary>
		static void Sweep( S.PolygonMesh m, IList<S.Vector3> line, Func<int, IList<(float x, float z)>> outline, S.Vector3? fixedSide = null )
		{
			var rings = new List<VertexHandle[]>();
			var sides = new List<S.Vector3>();
			var ups = new List<S.Vector3>();
			var tangents = new List<S.Vector3>();

			for ( int i = 0; i < line.Count; i++ )
			{
				var t = (line[Math.Min( line.Count - 1, i + 1 )] - line[Math.Max( 0, i - 1 )]).Normal;
				S.Vector3 side;
				if ( fixedSide.HasValue ) side = fixedSide.Value.Normal;
				else
				{
					var reference = MathF.Abs( t.z ) > 0.9f ? new S.Vector3( 1, 0, 0 ) : S.Vector3.Up;
					side = S.Vector3.Cross( reference, t ).Normal;
				}
				var up = S.Vector3.Cross( t, side ).Normal;
				var shape = outline( i );
				rings.Add( m.AddVertices( shape.Select( q => line[i] + side * q.x + up * q.z ).ToArray() ) );
				sides.Add( side );
				ups.Add( up );
				tangents.Add( t );
			}

			var count = rings[0].Length;
			for ( int i = 0; i + 1 < rings.Count; i++ )
			{
				var shape = outline( i );
				for ( int k = 0; k < count; k++ )
				{
					var n = (k + 1) % count;
					var dx = shape[n].x - shape[k].x;
					var dz = shape[n].z - shape[k].z;
					var outward = (sides[i] + sides[i + 1]) * dz - (ups[i] + ups[i + 1]) * dx;
					Face( m, new[] { rings[i][k], rings[i][n], rings[i + 1][n], rings[i + 1][k] }, outward );
				}
			}

			Face( m, rings[0], tangents[0] * -1 );
			Face( m, rings[^1], tangents[^1] );
		}

		/// <summary>
		/// Spin an outline round an upright axis: (r, z) points going round the solid
		/// anticlockwise, starting and ending on the axis (r = 0). With
		/// <paramref name="closedLoop"/> it's a ring instead (no point on the axis).
		/// <paramref name="axisSide"/> turns the axis to lie along that direction.
		/// </summary>
		static void Lathe( S.PolygonMesh m, S.Vector3 at, IList<(float r, float z)> outline, int sides, bool closedLoop = false, S.Vector3? axisSide = null, Func<int, float> sideScale = null )
		{
			var axis = S.Vector3.Up;
			var e1 = new S.Vector3( 1, 0, 0 );
			var e2 = new S.Vector3( 0, 1, 0 );
			if ( axisSide.HasValue )
			{
				axis = axisSide.Value.Normal;
				e1 = S.Vector3.Cross( axis, S.Vector3.Up ).Normal;
				e2 = S.Vector3.Cross( axis, e1 ).Normal;
			}

			S.Vector3 Spin( float r, float z, float a ) => at + (e1 * MathF.Cos( a ) + e2 * MathF.Sin( a )) * r + axis * z;
			var angles = Enumerable.Range( 0, sides ).Select( i => i * MathF.PI * 2 / sides ).ToArray();

			// A ring of vertices per outline point (one vertex for a point on the axis)
			var rings = outline.Select( q => q.r < 1e-3f
				? m.AddVertices( Spin( 0, q.z, 0 ) )
				: m.AddVertices( angles.Select( ( a, k ) => Spin( q.r * (sideScale?.Invoke( k ) ?? 1), q.z, a ) ).ToArray() ) ).ToList();

			var segments = closedLoop ? outline.Count : outline.Count - 1;
			for ( int i = 0; i < segments; i++ )
			{
				var j = (i + 1) % outline.Count;
				var a = rings[i];
				var b = rings[j];
				var dr = outline[j].r - outline[i].r;
				var dz = outline[j].z - outline[i].z;
				if ( a.Length == 1 && b.Length == 1 ) continue;

				for ( int k = 0; k < sides; k++ )
				{
					var n = (k + 1) % sides;
					var mid = angles[k] + MathF.PI / sides;
					var outward = (e1 * MathF.Cos( mid ) + e2 * MathF.Sin( mid )) * dz - axis * dr;
					if ( a.Length == 1 ) Face( m, new[] { a[0], b[n], b[k] }, outward );
					else if ( b.Length == 1 ) Face( m, new[] { a[k], a[n], b[0] }, outward );
					else Face( m, new[] { a[k], a[n], b[n], b[k] }, outward );
				}
			}
		}

		static void Box( S.PolygonMesh m, S.Vector3 center, S.Vector3 size, float yaw )
		{
			var x = new S.Vector3( MathF.Cos( yaw ), MathF.Sin( yaw ), 0 ) * (size.x / 2);
			var y = new S.Vector3( -MathF.Sin( yaw ), MathF.Cos( yaw ), 0 ) * (size.y / 2);
			var z = new S.Vector3( 0, 0, size.z / 2 );
			var v = m.AddVertices( Enumerable.Range( 0, 8 ).Select( i => center + x * ((i & 1) == 0 ? -1 : 1) + y * ((i & 2) == 0 ? -1 : 1) + z * ((i & 4) == 0 ? -1 : 1) ).ToArray() );
			Face( m, new[] { v[0], v[1], v[3], v[2] }, z * -1 );
			Face( m, new[] { v[4], v[5], v[7], v[6] }, z );
			Face( m, new[] { v[0], v[1], v[5], v[4] }, y * -1 );
			Face( m, new[] { v[2], v[3], v[7], v[6] }, y );
			Face( m, new[] { v[0], v[2], v[6], v[4] }, x * -1 );
			Face( m, new[] { v[1], v[3], v[7], v[5] }, x );
		}

		/// <summary>A lumpy boulder: an icosahedron with its corners pushed in and out, squashed.</summary>
		static void Rock( S.PolygonMesh m, S.Vector3 center, float radius, System.Random random )
		{
			var g = (1 + MathF.Sqrt( 5 )) / 2;
			var corners = new[]
			{
				new S.Vector3( -1, g, 0 ), new S.Vector3( 1, g, 0 ), new S.Vector3( -1, -g, 0 ), new S.Vector3( 1, -g, 0 ),
				new S.Vector3( 0, -1, g ), new S.Vector3( 0, 1, g ), new S.Vector3( 0, -1, -g ), new S.Vector3( 0, 1, -g ),
				new S.Vector3( g, 0, -1 ), new S.Vector3( g, 0, 1 ), new S.Vector3( -g, 0, -1 ), new S.Vector3( -g, 0, 1 ),
			};
			var spin = (float)random.NextDouble() * MathF.PI;
			var points = corners.Select( p =>
			{
				var q = p.Normal * (radius * (0.75f + (float)random.NextDouble() * 0.45f));
				q = new S.Vector3( q.x * MathF.Cos( spin ) - q.y * MathF.Sin( spin ), q.x * MathF.Sin( spin ) + q.y * MathF.Cos( spin ), q.z * 0.62f );
				return center + q;
			} ).ToArray();
			var v = m.AddVertices( points );
			int[,] faces =
			{
				{ 0, 11, 5 }, { 0, 5, 1 }, { 0, 1, 7 }, { 0, 7, 10 }, { 0, 10, 11 }, { 1, 5, 9 }, { 5, 11, 4 }, { 11, 10, 2 }, { 10, 7, 6 }, { 7, 1, 8 },
				{ 3, 9, 4 }, { 3, 4, 2 }, { 3, 2, 6 }, { 3, 6, 8 }, { 3, 8, 9 }, { 4, 9, 5 }, { 2, 4, 11 }, { 6, 2, 10 }, { 8, 6, 7 }, { 9, 8, 1 },
			};
			for ( int i = 0; i < 20; i++ )
			{
				var f = new[] { v[faces[i, 0]], v[faces[i, 1]], v[faces[i, 2]] };
				var mid = (points[faces[i, 0]] + points[faces[i, 1]] + points[faces[i, 2]]) * (1f / 3);
				Face( m, f, mid - center );
			}
		}
	}
}
