namespace FM24FaceAssistant;
public sealed class GameLifetimeGate
{
    bool observed; int absent;
    public bool Observe(bool running)
    {
        if(running){observed=true;absent=0;return false;}
        return observed&&++absent>=2;
    }
}
