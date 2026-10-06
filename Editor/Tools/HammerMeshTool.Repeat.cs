using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Repeat Last (Shift+G): do the last edit again on the current selection. Operations are
	/// remembered when run from a button or a key; a move, rotate or scale drag is remembered by
	/// how far it went, so repeating an extrude drag extrudes again by the same amount.
	/// </summary>
	partial class HammerMeshTool
	{
		Action _lastAction;

		/// <summary>
		/// What Repeat Last would do, for the panel (null when there's nothing yet).
		/// </summary>
		public string LastActionName { get; private set; }

		public void Remember( string name, Action action )
		{
			LastActionName = name;
			_lastAction = action;

			_history.Add( (name, action) );
			if ( _history.Count > 100 ) _history.RemoveAt( 0 );
			HistoryChanged?.Invoke();
		}

		readonly System.Collections.Generic.List<(string Name, Action Action)> _history = new();

		/// <summary>
		/// Every remembered command, oldest first (the Command History pane).
		/// </summary>
		public System.Collections.Generic.IReadOnlyList<(string Name, Action Action)> History => _history;

		public static event Action HistoryChanged;

		public void ClearHistory()
		{
			_history.Clear();
			HistoryChanged?.Invoke();
		}

		/// <summary>
		/// Run the picked commands, in order, the given number of times (each its own undo step).
		/// </summary>
		public void RepeatCommands( System.Collections.Generic.IList<int> indices, int times )
		{
			var actions = indices.Where( i => i >= 0 && i < _history.Count ).Select( i => _history[i].Action ).ToList();
			for ( int t = 0; t < times; t++ )
			{
				foreach ( var action in actions )
				{
					Undo.IncrementCurrentGroup();
					action();
				}
			}
			HammerViews.RepaintAll();
		}

		public void RepeatLast()
		{
			if ( _lastAction == null )
			{
				MeshHealth.Report( "Nothing to repeat yet" );
				return;
			}

			_lastAction();
			HammerViews.RepaintAll();
		}

		// ── Drags ──

		enum DragKind { None, Move, Rotate, Scale }

		DragKind _dragKind;
		bool _dragExtrude;
		Vector3 _dragDelta;
		Quaternion _dragRotation;
		Quaternion _dragBasis;
		Vector3 _dragScale;
		Vector3 _dragPivotOffset;
		Vector3 _dragPivot;

		void TrackDragStart( bool extrude )
		{
			_dragKind = DragKind.None;
			_dragExtrude = extrude;
		}

		void TrackMove( Vector3 delta )
		{
			_dragKind = DragKind.Move;
			_dragDelta = delta;
		}

		void TrackRotate( Vector3 pivot, Quaternion delta )
		{
			_dragKind = DragKind.Rotate;
			_dragRotation = delta;
			_dragPivot = pivot;
		}

		void TrackScale( Vector3 pivot, Quaternion basis, Vector3 scale )
		{
			_dragKind = DragKind.Scale;
			_dragBasis = basis;
			_dragScale = scale;
			_dragPivotOffset = pivot - _pivotStart;
		}

		/// <summary>
		/// At the end of a drag: remember it, if it moved anything.
		/// </summary>
		void RememberDrag()
		{
			RememberForTyping();
			var extrude = _dragExtrude;
			var offset = _dragPivotOffset;

			switch ( _dragKind )
			{
				case DragKind.Move:
				{
					var delta = _dragDelta;
					if ( delta.sqrMagnitude < 1e-12f ) return;
					Remember( extrude ? "Extrude" : "Move", () => RepeatDrag( extrude, pivot => Translate( pivot, delta ) ) );
					break;
				}

				case DragKind.Rotate:
				{
					// Round the same point again: with the pivot set off to one side, each repeat
					// carries on round the curve (Hammer's curved corridors: Shift+rotate, then Shift+G)
					var rotation = _dragRotation;
					var pivot = _dragPivot;
					if ( Quaternion.Angle( rotation, Quaternion.identity ) < 1e-3f ) return;
					Remember( extrude ? "Extrude + Rotate" : "Rotate", () => RepeatDrag( extrude, _ => ApplyRotate( pivot, rotation ) ) );
					break;
				}

				case DragKind.Scale:
				{
					var basis = _dragBasis;
					var scale = _dragScale;
					if ( (scale - Vector3.one).sqrMagnitude < 1e-12f ) return;
					Remember( extrude ? "Extrude + Scale" : "Scale", () => RepeatDrag( extrude, pivot => ApplyScale( pivot + offset, basis, scale ) ) );
					break;
				}
			}
		}

		// ── Typing an exact amount after a drag (Hammer: type on the numpad, Enter) ──

		DragKind _typedKind;
		bool _typedExtrude;
		Vector3 _typedDirection;
		Vector3 _typedAxis;
		Vector3 _typedPivot;
		Quaternion _typedBasis;
		Vector3 _typedScaleMask;
		string _typed;

		/// <summary>
		/// The number being typed after a drag, for the status bar (null when not typing).
		/// </summary>
		public string TypedValue => _typed;

		/// <summary>
		/// Remember the drag that just ended, so a typed number can redo it exactly.
		/// </summary>
		void RememberForTyping()
		{
			_typed = null;
			_typedKind = _dragKind;
			_typedExtrude = _dragExtrude;

			switch ( _dragKind )
			{
				case DragKind.Move:
					_typedDirection = _dragDelta.normalized;
					break;
				case DragKind.Rotate:
					_dragRotation.ToAngleAxis( out _, out _typedAxis );
					_typedPivot = _dragPivot;
					break;
				case DragKind.Scale:
					_typedBasis = _dragBasis;
					_typedPivot = _pivotStart + _dragPivotOffset;
					_typedScaleMask = new Vector3( Mathf.Abs( _dragScale.x - 1 ) > 1e-4f ? 1 : 0, Mathf.Abs( _dragScale.y - 1 ) > 1e-4f ? 1 : 0, Mathf.Abs( _dragScale.z - 1 ) > 1e-4f ? 1 : 0 );
					break;
			}
		}

		/// <summary>
		/// A key typed after a drag. Returns true if it was part of a number (or Enter / Esc for one).
		/// </summary>
		public bool TypeKey( KeyCode key, char character )
		{
			if ( _typedKind == DragKind.None ) return false;

			if ( key is KeyCode.Return or KeyCode.KeypadEnter && _typed != null ) { ApplyTyped(); return true; }
			if ( key == KeyCode.Escape && _typed != null ) { _typed = null; HammerViews.RepaintAll(); return true; }
			if ( key == KeyCode.Backspace && !string.IsNullOrEmpty( _typed ) ) { _typed = _typed.Substring( 0, _typed.Length - 1 ); HammerViews.RepaintAll(); return true; }

			var c = key switch
			{
				>= KeyCode.Keypad0 and <= KeyCode.Keypad9 => (char)('0' + (key - KeyCode.Keypad0)),
				KeyCode.KeypadPeriod => '.',
				KeyCode.KeypadMinus => '-',
				_ => '\0',
			};
			if ( c == '\0' ) return false;

			_typed = (_typed ?? "") + c;
			HammerViews.RepaintAll();
			return true;
		}

		/// <summary>
		/// Undo the last drag and do it again by the typed amount: units for a move, degrees for a
		/// rotation, a factor for a scale.
		/// </summary>
		void ApplyTyped()
		{
			var text = _typed;
			_typed = null;
			if ( !float.TryParse( text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value ) )
				return;

			var kind = _typedKind;
			var extrude = _typedExtrude;
			var direction = _typedDirection;
			var axis = _typedAxis;
			var pivot = _typedPivot;
			var basis = _typedBasis;
			var mask = _typedScaleMask;

			Undo.PerformUndo();

			switch ( kind )
			{
				case DragKind.Move:
					RepeatDrag( extrude, pivot => Translate( pivot, direction * value * SourceSpace.UnitScale ) );
					break;
				case DragKind.Rotate:
					RepeatDrag( extrude, _ => ApplyRotate( pivot, Quaternion.AngleAxis( value, axis ) ) );
					break;
				case DragKind.Scale:
					var scale = new Vector3( mask.x > 0 ? value : 1, mask.y > 0 ? value : 1, mask.z > 0 ? value : 1 );
					RepeatDrag( extrude, _ => ApplyScale( pivot, basis, scale ) );
					break;
			}

			// The redone drag can be typed over again
			HammerViews.RepaintAll();
		}

		// ── The gizmo drags, done from code (the tutorial map builds with these) ──

		/// <summary>Drag the move handle by <paramref name="delta"/> (world); Shift extrudes first.</summary>
		internal void ScriptedMove( Vector3 delta, bool extrude = false ) => RepeatDrag( extrude, pivot => Translate( pivot, delta ) );

		/// <summary>Turn the selection about <paramref name="pivot"/>; Shift extrudes first.</summary>
		internal void ScriptedRotate( Vector3 pivot, Quaternion rotation, bool extrude = false ) => RepeatDrag( extrude, _ => ApplyRotate( pivot, rotation ) );

		/// <summary>Scale the selection about its middle (or the pivot) along world axes; Shift extrudes first.</summary>
		internal void ScriptedScale( Vector3 scale, bool extrude = false ) => RepeatDrag( extrude, pivot => ApplyScale( pivot, Quaternion.identity, scale ) );

		/// <summary>A move without the gizmo: the handle (and a placed pivot) go along, as when dragged.</summary>
		void Translate( Vector3 pivot, Vector3 delta )
		{
			ApplyTranslate( delta );
			_handlePosition = pivot + delta;
		}

		/// <summary>Place the pivot (Insert), or clear it with null.</summary>
		internal void ScriptedPivot( Vector3? pivot )
		{
			RecordPivot();
			_customPivot = pivot;
			_pivotSelectionVersion = Selection.Version;
		}

		/// <summary>
		/// Run a drag again in one go: the same begin / apply / end a mouse drag goes through.
		/// </summary>
		void RepeatDrag( bool extrude, Action<Vector3> apply )
		{
			if ( Selection.Count == 0 ) return;

			_handlePosition = _customPivot ?? SelectionCenter();
			var pivot = _handlePosition;
			BeginTransform( extrude );
			apply( pivot );
			RebuildNow( Selection.Components );
			EndTransform();
		}
	}
}
