using UnityEngine;

namespace VLabOrchestrator.Showcase
{
    public sealed class ShowcaseSharedInstrumentDefinition : MonoBehaviour
    {
        [SerializeField] private string objectId = "generic_instrument_01";
        [SerializeField] private Transform controlledPart;
        [SerializeField] private Renderer accessIndicator;

        public string ObjectId => objectId;
        public Transform ControlledPart => controlledPart;
        public Renderer AccessIndicator => accessIndicator;

        public void Configure(string stableObjectId, Transform controlled, Renderer indicator)
        {
            objectId = stableObjectId;
            controlledPart = controlled;
            accessIndicator = indicator;
        }

        private void OnValidate()
        {
            if (controlledPart == null)
                controlledPart = transform.Find("Controlled Turret");
            if (accessIndicator == null)
            {
                Transform candidate = transform.Find("Access Indicator");
                if (candidate != null)
                    accessIndicator = candidate.GetComponent<Renderer>();
            }
        }
    }
}
