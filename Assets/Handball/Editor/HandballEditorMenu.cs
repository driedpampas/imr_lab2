using UnityEditor;
using UnityEditor.SceneManagement;

namespace Handball.EditorTools
{
    public static class HandballEditorMenu
    {
        [MenuItem("Handball/Open Scene")]
        public static void OpenScene()
        {
            EditorSceneManager.OpenScene(HandballSceneBuilder.ScenePath);
        }
    }
}
