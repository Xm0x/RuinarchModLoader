using UnityEngine;

namespace Ruinarch.ModContent.Templates
{
	// Keep the source identity when a rebuilt prefab is exported again.
	internal sealed class TemplateWallLayout : MonoBehaviour
	{
		[SerializeField] internal string Source;
	}
}
