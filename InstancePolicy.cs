using System;
using System.Threading;
namespace FM24FaceAssistant;
public static class InstancePolicy
{
    public static bool Acquire(Mutex mutex)
    {
        try{return mutex.WaitOne(0);}catch(AbandonedMutexException){return true;}
    }
    public static bool ShouldReplace(Version candidate,Version running)=>candidate>running;
}
