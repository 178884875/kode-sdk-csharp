namespace KodaClaw.Contracts;

[Flags]
public enum ModelCapabilitySet
{
    None            = 0,
    TextChat        = 1 << 0,
    ToolCalling     = 1 << 1,
    Vision          = 1 << 2,
    ImageGeneration = 1 << 3,
    TextToSpeech    = 1 << 4,
    SpeechToText    = 1 << 5,
    Embeddings      = 1 << 6,
}
