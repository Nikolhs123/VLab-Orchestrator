using UnityEngine;

namespace VLabOrchestrator.Showcase
{
    public sealed class ShowcaseParticipantDefinition : MonoBehaviour
    {
        [SerializeField] private string participantId = "Researcher";
        [SerializeField] private string displayName = "Researcher";
        [SerializeField] private Renderer[] visualRenderers;

        public string ParticipantId => participantId;
        public string DisplayName => displayName;
        public Renderer[] VisualRenderers => visualRenderers;

        public void Configure(string stableParticipantId, string participantDisplayName, Renderer[] visuals)
        {
            participantId = stableParticipantId;
            displayName = participantDisplayName;
            visualRenderers = visuals;
        }

        public void ApplyMaterial(Material material)
        {
            if (material == null || visualRenderers == null)
                return;

            foreach (Renderer visual in visualRenderers)
            {
                if (visual != null)
                    visual.sharedMaterial = material;
            }
        }

        private void OnValidate()
        {
            if (visualRenderers == null || visualRenderers.Length == 0)
                visualRenderers = GetComponentsInChildren<Renderer>(true);
        }
    }
}
