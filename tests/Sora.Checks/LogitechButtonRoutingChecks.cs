using Sora.Core;

static class LogitechButtonRoutingChecks
{
    public static async Task Run()
    {
        static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
        var profile=LogitechControlProfile.Create();
        var map=LogitechButtonRouting.NativeMap(profile);
        Require(Convert.ToHexString(map)=="01020304050000000000000000000000","Default standard buttons must remain native.");
        // Reproduce the old policy: only left-click was forwarded by hardware.
        var legacy=new byte[16];legacy[0]=1;
        Require(legacy[1]==0 && map[1]==2,"Legacy right-click suppression fixture changed.");
        for(var button=1;button<5;button++)
        {
            Require(LogitechButtonRouting.IsNative(profile,button),"A default button lost native routing.");
            var keys=profile.Buttons.ToArray();keys[button]=new("Key","Custom",[65]);
            var custom=profile with {Buttons=keys};
            Require(LogitechButtonRouting.NativeMap(custom)[button]==0,"A remap must not also emit its native button.");
            Require(LogitechButtonRouting.RuntimeAction(custom,button,false).Kind=="Key","Base remap was lost.");
            var alternate=profile.ShiftButtons.ToArray();alternate[button]=new("Key","Shift custom",[66]);
            var shifted=profile with {ShiftButtons=alternate};
            Require(LogitechButtonRouting.NativeMap(shifted)[button]==0,"An alternate remap cannot simultaneously keep native forwarding.");
            Require(LogitechButtonRouting.RuntimeAction(shifted,button,false).Kind==profile.Buttons[button].Kind,"Base action was lost when its shifted action differs.");
            Require(LogitechButtonRouting.RuntimeAction(shifted,button,true).Kind=="Key","Shifted remap was lost.");
            alternate[button]=profile.Buttons[button];
            Require(LogitechButtonRouting.NativeMap(profile with {ShiftButtons=alternate})[button]==button+1,"Explicit matching alternate action must stay native.");
            Require(!LogitechButtonRouting.CanChangeWhileHeld(profile,custom,(ushort)(1<<button)),"Routing changed while its physical button was held.");
            Require(!LogitechButtonRouting.CanChangeWhileHeld(custom,profile,(ushort)(1<<button)),"Reverse routing changed while held.");
            Require(LogitechButtonRouting.CanChangeWhileHeld(profile,custom,0),"Released buttons incorrectly prevent switching.");
        }
        var sink=new RejectInjectedMouse();
        await using(var runner=new LogitechActionRunner(sink,profile))
        {
            var state=new LogitechButtonState(profile);
            foreach(var transition in state.Update(0x1e)) await runner.HandleAsync(transition.Button,transition.Action,transition.Down);
            foreach(var transition in state.Update(0)) await runner.HandleAsync(transition.Button,transition.Action,transition.Down);
            Require(sink.MouseCalls==0,"Native standard buttons must never be duplicated through Windows mouse injection.");
        }
        var actions=profile.Buttons.ToArray();actions[1]=new("Key","A",[65]);
        var remapped=profile with {Buttons=actions};var output=new RejectInjectedMouse();
        await using(var runner=new LogitechActionRunner(output,remapped))
        {
            var state=new LogitechButtonState(remapped);
            foreach(var transition in state.Update(2)) await runner.HandleAsync(transition.Button,transition.Action,transition.Down);
            foreach(var transition in state.Update(0)) await runner.HandleAsync(transition.Button,transition.Action,transition.Down);
            Require(output.Keys.SequenceEqual(new[]{"65 down","65 up"}),"A genuine remap must still press/release once.");
        }
        var held=new LogitechButtonState(profile);held.Update(2);held.UseProfile(remapped);
        Require(held.Update(0).Single().Action.Kind=="Native","Release must retain the native action captured at press time.");
        actions=profile.Buttons.ToArray();actions[3]=new("MouseRight","Right on Back");
        Require(LogitechButtonRouting.NativeMap(profile with {Buttons=actions})[3]==0,"A mouse action on another physical button is a remap.");
        actions=profile.Buttons.ToArray();actions[0]=new("Disabled","Bad");
        try {LogitechButtonRouting.NativeMap(profile with {Buttons=actions});throw new Exception("Expected primary refusal.");} catch(InvalidDataException) {}
        try {LogitechButtonRouting.NativeMap(profile with {Buttons=[]});throw new Exception("Expected malformed layer refusal.");} catch(InvalidDataException) {}
    }
    private sealed class RejectInjectedMouse:ILogitechOutput
    {
        public int MouseCalls;public List<string> Keys=[];
        public void Mouse(string kind,bool down) {MouseCalls++;throw new IOException("Simulated unavailable Windows mouse injection.");}
        public void Key(int key,bool down) => Keys.Add(key+(down?" down":" up"));
        public void Scroll(bool horizontal,int delta) {}
        public void Launch(string target,bool url) {}
        public void ReleaseAll() {}
    }
}
