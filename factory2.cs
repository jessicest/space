IMyPistonBase _factory_x;
IMyPistonBase _factory_y;
IMyPistonBase _factory_z;
IMyShipController _seat;
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
        _factory_x = (IMyPistonBase) LoadBlock("Factory X Piston");
        _factory_y = (IMyPistonBase) LoadBlock("Factory Y Piston");
        _factory_z = (IMyPistonBase) LoadBlock("Factory Z Piston");
        _seat = (IMyShipController) LoadBlock("Factory seat");
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

    Vector3 command = _seat.MoveIndicator;
    float roll = _seat.RollIndicator;

    if(command != null) {
        if(command.X < 0.0f) {
            _factory_x.Velocity = 1.0f;
        } else if(command.X > 0.0f) {
            _factory_x.Velocity = -1.0f;
        } else {
            _factory_x.Velocity = 0.0f;
        }

        if(command.Y < 0.0f) {
            _factory_y.Velocity = -1.0f;
        } else if(command.Y > 0.0f) {
            _factory_y.Velocity = 1.0f;
        } else {
            _factory_y.Velocity = 0.0f;
        }

        if(command.Z < 0.0f) {
            _factory_z.Velocity = 1.0f;
        } else if(command.Z > 0.0f) {
            _factory_z.Velocity = -1.0f;
        } else {
            _factory_z.Velocity = 0.0f;
        }
    }
}
