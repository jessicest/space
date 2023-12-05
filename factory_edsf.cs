IMyPistonBase heightener;
IMyPistonBase reachener;
IMyShipController controller;
float epsilon = 0.00001f;

public Program()
{
    Runtime.UpdateFrequency |= UpdateFrequency.Update10;
    heightener = GridTerminalSystem.GetBlockWithName("Welder heightener") as IMyPistonBase;
    reachener = GridTerminalSystem.GetBlockWithName("Welder reachener") as IMyPistonBase;
    controller = GridTerminalSystem.GetBlockWithName("Welder control seat") as IMyShipController;
    if (heightener == null || reachener == null || controller == null) 
    {
        Echo("Oh my! I couldn't find that block. " + heightener + ", " + reachener + ", " + controller);
        return;
    }
}

public void Main(string argument, UpdateType updateSource)
{
    Vector3 command = controller.MoveIndicator;
    if(command != null) {
        if(command.X < 0.0f) {
            reachener.Velocity = -1.0f;
        } else if(command.X > 0.0f) {
            reachener.Velocity = 1.0f;
        } else {
            reachener.Velocity = 0.0f;
        }

        if(command.Z < 0.0f) {
            heightener.Velocity = 1.0f;
        } else if(command.Z > 0.0f) {
            heightener.Velocity = -1.0f;
        } else {
            heightener.Velocity = 0.0f;
        }
    }
}
