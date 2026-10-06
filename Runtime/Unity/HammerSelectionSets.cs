using System;
using System.Collections.Generic;
using UnityEngine;

namespace HammerUnity
{
	/// <summary>
	/// Hammer's selection sets for one scene: named groups of objects to select, hide or show
	/// together. Lives on an editor-only object in the scene so the sets are saved with it.
	/// </summary>
	[AddComponentMenu( "" )]
	public sealed class HammerSelectionSets : MonoBehaviour
	{
		[Serializable]
		public sealed class Set
		{
			public string Name;
			public List<GameObject> Objects = new();
			public bool Hidden;
		}

		public List<Set> Sets = new();

		/// <summary>
		/// The scene's sets, made on first use.
		/// </summary>
		public static HammerSelectionSets Get( bool create )
		{
			var existing = FindFirstObjectByType<HammerSelectionSets>( FindObjectsInactive.Include );
			if ( existing != null || !create ) return existing;

			var go = new GameObject( "Hammer Selection Sets" ) { tag = "EditorOnly" };
			return go.AddComponent<HammerSelectionSets>();
		}
	}
}
