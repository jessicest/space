IMyPistonBase weldHeightener;
IMyPistonBase weldReachener;
IMyPistonBase camHeightener;
IMyPistonBase camReachener;
IMyMotorAdvancedStator camHinge;
IMyShipController controller;
bool broken;

private IMyTerminalBlock LoadBlock(string name) {
    var block = GridTerminalSystem.GetBlockWithName(name);
    if(block == null) {
        broken = true;
        throw new Exception("Oh shit! I can't find block: '" + name + "'");
    }
    return block;
}

public Program()
{
    Runtime.UpdateFrequency |= UpdateFrequency.Update10;
    try {
        weldHeightener = (IMyPistonBase) LoadBlock("Welder heightener");
        weldHeightener = (IMyPistonBase) LoadBlock("Welder heightener");
        weldReachener = (IMyPistonBase) LoadBlock("Welder reachener");
        camHeightener = (IMyPistonBase) LoadBlock("Weldcam heightener");
        camReachener = (IMyPistonBase) LoadBlock("Weldcam reachener");
        camHinge = (IMyMotorAdvancedStator) LoadBlock("Weldcam hinge");
        controller = (IMyShipController) LoadBlock("Welder control seat");
        Echo("ok coolio!");
    } catch(Exception e) {
        Echo(e.Message);
        broken = true;
        return;
    }
}

public void Main(string argument, UpdateType updateSource)
{
    if(broken) {
        return;
    }

    Vector3 command = controller.MoveIndicator;
    float roll = controller.RollIndicator;

    if(command != null) {
        if(command.X < 0.0f) {
            weldReachener.Velocity = -1.0f;
        } else if(command.X > 0.0f) {
            weldReachener.Velocity = 1.0f;
        } else {
            weldReachener.Velocity = 0.0f;
        }

        if(command.Z < 0.0f) {
            weldHeightener.Velocity = 1.0f;
            camHeightener.Velocity = 1.0f;
        } else if(command.Z > 0.0f) {
            weldHeightener.Velocity = -1.0f;
            camHeightener.Velocity = -1.0f;
        } else {
            weldHeightener.Velocity = 0.0f;
            camHeightener.Velocity = 0.0f;
        }

        if(roll < 0.0f) {
            camReachener.Velocity = 2.8f;
            camHinge.TargetVelocityRPM = 2.0f;
        } else if(roll > 0.0f) {
            camReachener.Velocity = -2.8f;
            camHinge.TargetVelocityRPM = -2.0f;
        } else {
            camReachener.Velocity = 0.0f;
            camHinge.TargetVelocityRPM = 0.0f;
        }
    }
}
