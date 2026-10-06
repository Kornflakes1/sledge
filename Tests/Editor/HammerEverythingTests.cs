using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HammerUnity.EditorTools;
using NUnit.Framework;
using Sandbox.Primitives;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using S = Sandbox;

namespace HammerUnity.Tests
{
	/// <summary>
	/// Every operation the tool has, in every selection mode, with one element and with
	/// everything selected, on awkward shapes (open, many-sided, triangles, inside out, tiny,
	/// huge, turned and stretched, two objects at once). Each must not throw, must leave sound
	/// meshes, and must undo and redo in one step each. Everything found is listed in one report.
	/// </summary>
	public class HammerEverythingTests
	{
		HammerMeshTool _tool;
		readonly List<GameObject> _made = new();

		[SetUp]
		public void SetUp()
		{
			Undo.ClearAll();
			_tool = ScriptableObject.CreateInstance<HammerMeshTool>();
			_tool.Attach();
		}

		[TearDown]
		public void TearDown()
		{
			_tool.SubTool?.Cancel();
			_tool.Detach();
			Object.DestroyImmediate( _tool );
			Clean();
			UnityEditor.Selection.objects = new Object[0];
			Undo.ClearAll();
		}

		void Clean()
		{
			foreach ( var c in HammerMesh.Enabled.ToList() )
				if ( c != null ) Object.DestroyImmediate( c.gameObject );
			_made.Clear();
		}

		// ── Awkward shapes ──

		static S.PolygonMesh Block( float size ) => new BlockPrimitive().CreateMesh( new S.BBox( new S.Vector3( -size / 2 ), new S.Vector3( size / 2 ) ) );

		static S.PolygonMesh Prism( int sides, float radius, float height )
		{
			var mesh = new S.PolygonMesh();
			var bottom = Enumerable.Range( 0, sides ).Select( i => new S.Vector3( Mathf.Cos( i * Mathf.PI * 2 / sides ) * radius, Mathf.Sin( i * Mathf.PI * 2 / sides ) * radius, 0 ) ).ToArray();
			var b = mesh.AddVertices( bottom );
			var t = mesh.AddVertices( bottom.Select( p => p + new S.Vector3( 0, 0, height ) ).ToArray() );
			mesh.AddFace( t );
			mesh.AddFace( b.Reverse().ToArray() );
			for ( int i = 0; i < sides; i++ )
			{
				var j = (i + 1) % sides;
				mesh.AddFace( b[i], b[j], t[j], t[i] );
			}
			return mesh;
		}

		static readonly (string Name, Func<S.PolygonMesh> Make, Action<GameObject> Place)[] Shapes =
		{
			("box", () => Block( 128 ), null),
			("open box", () => { var m = Block( 128 ); m.RemoveFaces( m.FaceHandles.Where( f => { m.ComputeFaceNormal( f, out var n ); return n.z > 0.9f; } ).ToList() ); return m; }, null),
			("hexagon prism (ngons)", () => Prism( 6, 64, 96 ), null),
			("wedge (triangles)", () => Prism( 3, 64, 96 ), null),
			("inside-out box", () => { var m = Block( 128 ); m.FlipAllFaces(); return m; }, null),
			("tiny box", () => Block( 1 ), null),
			("huge box", () => Block( 8192 ), null),
			("turned, stretched box", () => Block( 128 ), go => { go.transform.rotation = Quaternion.Euler( 20, 35, 10 ); go.transform.localScale = new Vector3( 1, 2, 0.5f ); }),
			("single quad", () => new QuadPrimitive().CreateMesh( new S.BBox( new S.Vector3( -64, -64, 0 ), new S.Vector3( 64, 64, 0 ) ) ), null),
		};

		HammerMesh Make( string name, S.PolygonMesh mesh, Vector3 position, Action<GameObject> place )
		{
			var go = new GameObject( name );
			_made.Add( go );
			go.transform.position = position;
			place?.Invoke( go );
			var c = go.AddComponent<HammerMesh>();
			c.Mesh = mesh;
			Undo.RegisterCreatedObjectUndo( go, "Create" );
			Step();
			return c;
		}

		static void Step()
		{
			Undo.FlushUndoRecordObjects();
			Undo.IncrementCurrentGroup();
		}

		// ── What to run ──

		// Things that aren't edits, need a view or the mouse, or ask a question
		static readonly HashSet<string> Skip = new()
		{
			"Attach", "Detach", "Cancel", "Confirm", "ClearHistory", "RepeatLast", "FrameSelection",
			"LiftMaterialUnderCursor", "BeginWorkplanePick", "PivotToViewCenter", "ClearPivot",
			"AlignToTarget", "RotateToTarget", // pick a target with the mouse
		};

		static List<MethodInfo> Operations() => typeof( HammerMeshTool )
			.GetMethods( BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly )
			.Where( m => m.ReturnType == typeof( void ) && m.GetParameters().Length == 0 && !m.IsSpecialName && !Skip.Contains( m.Name ) )
			.OrderBy( m => m.Name )
			.ToList();

		IEnumerable<IMeshElement> Elements( HammerMesh c, EditMode mode )
		{
			var mesh = c.Mesh;
			return mode switch
			{
				EditMode.Vertex => mesh.VertexHandles.Select( v => (IMeshElement)new MeshVertex( c, v ) ),
				EditMode.Edge => mesh.HalfEdgeHandles.Where( h => !mesh.GetOppositeHalfEdge( h ).IsValid || h.Index < mesh.GetOppositeHalfEdge( h ).Index ).Select( h => (IMeshElement)new MeshEdge( c, h ) ),
				_ => mesh.FaceHandles.Select( f => (IMeshElement)new MeshFace( c, f ) ),
			};
		}

		void SelectFor( EditMode mode, IList<HammerMesh> meshes, bool all )
		{
			_tool.Mode = mode;
			_tool.Selection.Clear();
			UnityEditor.Selection.objects = meshes.Select( m => (Object)m.gameObject ).ToArray();
			UnityEditor.Selection.activeGameObject = meshes[0].gameObject;
			if ( mode != EditMode.Object )
				foreach ( var m in meshes )
					foreach ( var e in all ? Elements( m, mode ) : Elements( m, mode ).Take( 1 ) )
						_tool.Selection.Add( e );
			Step();
		}

		// ── Checking ──

		static string Shape( HammerMesh c )
		{
			c.RebuildIfReloaded();
			var mesh = c.Mesh;
			var verts = mesh.VertexHandles.Select( v => mesh.GetVertexPosition( v ) ).Select( p => $"{p.x:0.##},{p.y:0.##},{p.z:0.##}" ).OrderBy( x => x );
			var faces = mesh.FaceHandles.Select( f => $"{mesh.GetFaceVertices( f ).Length}{(mesh.IsFaceHidden( f ) ? "h" : "")}" ).OrderBy( x => x );
			var t = c.transform;
			return $"{c.name}@{t.position.x:0.###},{t.position.y:0.###},{t.position.z:0.###}/{t.rotation.eulerAngles.y:0.#} {mesh.FaceHandles.Count()}f [{string.Join( " ", faces )}] {string.Join( " ", verts )}";
		}

		static string Scene() => string.Join( " | ", HammerMesh.Enabled.Where( c => c != null ).Select( Shape ).OrderBy( x => x ) );

		string SelectionText() => string.Join( " ", _tool.Selection.Where( x => x.IsValid ).Select( x => $"{x.GetType().Name[4]}{x.Index}" ).OrderBy( x => x ) )
			+ " / " + string.Join( " ", UnityEditor.Selection.gameObjects.Select( g => g.name ).OrderBy( x => x ) );

		/// <summary>New problems on a mesh: non-finite points, or broken faces it didn't have before.</summary>
		static string Problems( HammerMesh c, int badBefore )
		{
			var mesh = c.Mesh;
			if ( mesh.VertexHandles.Any( v => { var p = mesh.GetVertexPosition( v ); return !float.IsFinite( p.x ) || !float.IsFinite( p.y ) || !float.IsFinite( p.z ); } ) )
				return "non-finite vertex";
			var bad = mesh.FindBadFaces( includeNonPlanar: false );
			if ( bad.Count > badBefore ) return $"broken faces: {string.Join( ", ", bad.Values.Distinct() )}";
			var render = c.GetComponent<MeshFilter>().sharedMesh;
			if ( mesh.FaceHandles.Any( f => !mesh.IsFaceHidden( f ) ) && (render == null || render.triangles.Length == 0) ) return "rendered mesh is empty";
			return null;
		}

		[Test]
		public void EveryOperationOnEveryShape()
		{
			var ops = Operations();
			var failures = new List<string>();
			var findings = new List<string>();
			var expected = new List<string>();
			var ran = 0;
			var grid = HammerSettings.GridSize;

			var modes = new[] { EditMode.Vertex, EditMode.Edge, EditMode.Face, EditMode.Object };
			var shapes = Shapes.Select( s => (Name: s.Name, Make: s.Make, Place: s.Place, Two: false) ).ToList();
			shapes.Add( (Name: "two boxes at once", Make: () => Block( 96 ), Place: null, Two: true) );

			try
			{
				foreach ( var shape in shapes )
				foreach ( var mode in modes )
				foreach ( var all in new[] { false, true } )
				foreach ( var op in ops )
				{
					var label = $"{op.Name} | {mode} | {(all ? "all" : "one")} | {shape.Name}";
					Clean();
					Undo.ClearAll();
					HammerSettings.GridSize = 8;

					var meshes = new List<HammerMesh> { Make( "A", shape.Make(), Vector3.zero, shape.Place ) };
					if ( shape.Two ) meshes.Add( Make( "B", shape.Make(), new Vector3( 4, 0, 0 ), shape.Place ) );
					SelectFor( mode, meshes, all );

					var badBefore = meshes.ToDictionary( m => m, m => m.Mesh.FindBadFaces( includeNonPlanar: false ).Count );
					var before = Scene();
					var selBefore = SelectionText();

					try
					{
						op.Invoke( _tool, null );
						if ( _tool.SubTool != null ) _tool.SubTool.Apply(); // tools that open: take their defaults
						if ( _tool.SubTool != null ) _tool.SubTool.Cancel();
					}
					catch ( Exception e )
					{
						var inner = e is TargetInvocationException t ? t.InnerException : e;
						failures.Add( $"THROWS  {label}: {inner?.GetType().Name}: {inner?.Message} @ {inner?.StackTrace?.Split( '\n' ).FirstOrDefault()?.Trim()}" );
						continue;
					}
					Step();
					ran++;

					// Squashing points together is what these do when asked to (snap everything to the
					// last vertex, or a 1-unit box to an 8-unit grid): not a fault
					var squashes = op.Name == "SnapToLastVertex" || (op.Name == "SnapToGrid" && shape.Name == "tiny box") || (op.Name == "ExtrudeEdges" && shape.Name == "tiny box");
					foreach ( var c in HammerMesh.Enabled.Where( x => x != null ).ToList() )
					{
						var problem = Problems( c, badBefore.TryGetValue( c, out var n ) ? n : 0 );
						if ( problem != null ) (squashes ? expected : findings).Add( $"UNSOUND {label}: {c.name}: {problem}" );
					}

					var after = Scene();
					var selAfter = SelectionText();
					if ( after == before && selAfter == selBefore ) continue; // did nothing here: fine

					Undo.PerformUndo();
					if ( Scene() != before ) failures.Add( $"UNDO    {label}: one undo didn't restore the meshes" );
					else if ( SelectionText() != selBefore ) findings.Add( $"UNDO-SEL {label}: one undo restored the meshes but not the selection ({SelectionText()} vs {selBefore})" );

					Undo.PerformRedo();
					if ( Scene() != after ) failures.Add( $"REDO    {label}: one redo didn't restore the result" );
				}
			}
			finally
			{
				HammerSettings.GridSize = grid;
			}

			var report = $"Ran {ran} operation cases ({ops.Count} operations).\n\nFAILURES ({failures.Count}):\n{string.Join( "\n", failures )}\n\nFINDINGS ({findings.Count}):\n{string.Join( "\n", findings )}\n\nEXPECTED ({expected.Count}):\n{string.Join( "\n", expected )}";
			System.IO.File.WriteAllText( "everything-report.txt", report );
			Debug.Log( report );
			Assert.That( failures.Concat( findings ), Is.Empty, report );
		}
	
		static string Brief( string scene ) => string.Join( " | ", scene.Split( " | " ).Select( m => string.Join( " ", m.Split( ' ' ).Take( 2 ) ) + " " + m.Split( ' ' ).Count( x => x.Contains( ',' ) ) + "v" ) );

		[Test]
		public void RandomChainsUndoAllTheWayBackAndForward()
		{
			// Long random chains of operations on the awkward shapes, then undo every step back to
			// the start and redo every step forward again: each stop must match what it was
			var ops = Operations().Where( o => o.Name is not ("SnapToLastVertex" or "Delete" or "Collapse") ).ToList();
			var modes = new[] { EditMode.Vertex, EditMode.Edge, EditMode.Face, EditMode.Object };
			var failures = new List<string>();
			var grid = HammerSettings.GridSize;

			try
			{
				for ( int seed = 1; seed <= 24; seed++ )
				{
					var rng = new System.Random( seed );
					Clean();
					Undo.ClearAll();
					HammerSettings.GridSize = 8;

					var shape = Shapes[rng.Next( Shapes.Length )];
					var meshes = new List<HammerMesh> { Make( "A", shape.Make(), Vector3.zero, shape.Place ) };
					if ( rng.Next( 3 ) == 0 ) meshes.Add( Make( "B", Block( 96 ), new Vector3( 4, 0, 0 ), null ) );

					var states = new List<string> { Scene() };
					var names = new List<string>();

					for ( int step = 0; step < 14; step++ )
					{
						var live = HammerMesh.Enabled.Where( x => x != null ).ToList();
						if ( live.Count == 0 ) break;

						var mode = modes[rng.Next( modes.Length )];
						_tool.Mode = mode;
						_tool.Selection.Clear();
						UnityEditor.Selection.objects = live.Select( m => (Object)m.gameObject ).ToArray();
						if ( mode != EditMode.Object )
							foreach ( var m in live )
								foreach ( var e in Elements( m, mode ).Where( _ => rng.Next( 3 ) == 0 ).ToList() )
									_tool.Selection.Add( e );
						Step();

						var op = ops[rng.Next( ops.Count )];
						var before = Scene();
						try
						{
							op.Invoke( _tool, null );
							if ( _tool.SubTool != null ) _tool.SubTool.Apply();
							if ( _tool.SubTool != null ) _tool.SubTool.Cancel();
						}
						catch ( Exception e )
						{
							var inner = e is TargetInvocationException t ? t.InnerException : e;
							failures.Add( $"seed {seed} step {step} {op.Name} ({mode}, {shape.Name}): throws {inner?.GetType().Name}: {inner?.Message}" );
							break;
						}
						Step();

						var after = Scene();
						if ( after == before ) continue; // nothing changed: no undo step to count
						states.Add( after );
						names.Add( $"{op.Name} ({mode})" );
					}

					// All the way back, one Ctrl+Z per edit (selection changes and edits that did nothing are their own steps, skipped over)
					for ( int k = states.Count - 1; k > 0; k-- )
					{
						for ( int tries = 0; tries < 80 && Scene() == states[k]; tries++ ) Undo.PerformUndo();
						if ( Scene() != states[k - 1] )
						{
							failures.Add( $"seed {seed} ({shape.Name}): undoing '{names[k - 1]}' didn't give back the step before it\n    got  {Brief( Scene() )}\n    want {Brief( states[k - 1] )}\n    from {Brief( states[k] )}\n    ops  {string.Join( ", ", names )}" );
							break;
						}
					}

					// And all the way forward
					for ( int k = 1; k < states.Count; k++ )
					{
						for ( int tries = 0; tries < 80 && Scene() == states[k - 1]; tries++ ) Undo.PerformRedo();
						if ( Scene() != states[k] )
						{
							failures.Add( $"seed {seed} ({shape.Name}): redoing '{names[k - 1]}' didn't give back its result" );
							break;
						}
					}
				}
			}
			finally
			{
				HammerSettings.GridSize = grid;
			}

			System.IO.File.WriteAllText( "chains-report.txt", string.Join( "\n", failures ) );
			Assert.That( failures, Is.Empty, string.Join( "\n", failures ) );
		}
	}
}
