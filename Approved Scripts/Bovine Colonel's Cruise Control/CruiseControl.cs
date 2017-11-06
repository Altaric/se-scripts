/* 
 * Bovine's Planetary Cruise Control - Version 0.2
 *  
 * A simple cruise control script for maintaining 100 m/s in atmosphere while conserving power or hydrogen fuel. 
 * It is recommended that you use the script with inertial dampeners ON or with wings from an aerodynamics mod. 
 *  
 * If the ship is not under player control, all parachutes on the ship are deployed. 
 *  
 * Ship needs: 
 * - A programmable block with this script loaded 
 * - A timer block with actions Start on itself, Trigger Now on itself, and Run with Default Argument on the progrmammable block 
 * - A cockpit with a player in it, or a remote control that is being controlled by a player; this block must be able to control thrusters 
 *  
 * Planned features: 
 * - Cruise at current speed when enabled 
 * - Improve fuel efficiency if possible 
 *  
 * Known issues: 
 * - The script regularly exceeds target speed. In practice this means target speed should not be higher than around 90 m/s. 
 * - Setup happens every time script is enabled 
 */ 
 
// The rate per second at which the program runs. Higher value = more constant speed but also more server performance load. 
const double updateRate = 10; 
 
// The script's target speed in metres per second. The ship can go faster than this speed by facing downward. 
const double targetSpeed = 90; 
 
// ==== Please do not touch anything below ==== // 
 
bool enabled = false; 
int time = 0; 
const double period = 60 / updateRate; 
 
List<IMyShipController> controllers = new List<IMyShipController>(); // ship controllers - remotes, cockpits, and the like 
List<IMyThrust> fwdThrusters = new List<IMyThrust>(); // forward thrusters 
List<IMyThrust> bwdThrusters = new List<IMyThrust>(); // backward thrusters 
 
public void Main(string argument) 
{ 
    switch (argument.ToLower()) 
    { 
        case "on": 
        case "start": 
        case "enable": 
            Enable(); 
            break; 
        case "off": 
        case "stop": 
        case "disable": 
            Disable(); 
            break; 
        case "toggle": 
            if (enabled) Disable(); 
            else Enable(); 
            break; 
    } 
 
    Echo(time.ToString()); 
    Update(); 
} 
 
// Get a list of every cockpit or remote control available 
private void FindControllers() 
{ 
    GridTerminalSystem.GetBlocksOfType<IMyShipController>(controllers); 
} 
 
// Get a list of every thruster, filtered using the given filter 
private void FindThrusters(List<IMyThrust> thrusters, Func<IMyThrust, bool> filter = null) 
{ 
    List<IMyThrust> allThrusters = new List<IMyThrust>(); 
    GridTerminalSystem.GetBlocksOfType<IMyThrust>(allThrusters); 
 
    foreach (IMyThrust thruster in allThrusters) 
    { 
        if (filter == null || filter(thruster)) 
            thrusters.Add(thruster); 
    } 
} 
 
// Get a controller that is under control by a player, or null if there isn't one 
private IMyShipController getActiveController() 
{ 
    foreach (IMyTerminalBlock block in controllers) 
    { 
        IMyShipController controller = block as IMyShipController; 
        if (controller.IsUnderControl && controller.CanControlShip) 
        { 
            return controller; 
        } 
    } 
    return null; 
} 
 
// Performs setup functions and enables cruise control. 
private void Enable() 
{ 
    if (enabled) // do nothing if cruise is already active 
        return; 
 
    FindControllers(); 
    IMyShipController reference = getActiveController(); 
    if (reference == null) 
    { 
        return; // don't start cruise if ship is not being controlled 
    } 
 
    // get all forward thrusters 
    FindThrusters(fwdThrusters, thruster => 
    { 
        Vector3D fwd = reference.WorldMatrix.Forward; 
        Vector3D thrustDir = thruster.WorldMatrix.Backward; 
        return fwd == thrustDir; 
    }); 
 
    // get all backward thrusters 
    FindThrusters(bwdThrusters, thruster => 
    { 
        Vector3D bwd = reference.WorldMatrix.Backward; 
        Vector3D thrustDir = thruster.WorldMatrix.Backward; 
        return bwd == thrustDir; 
    }); 
 
    // turn backward thrusters off 
    bwdThrusters.ForEach(thruster => thruster.ApplyAction("OnOff_Off")); 
 
    enabled = true; 
} 
 
// Disabling cruise control turns off all thruster overrides 
private void Disable() 
{ 
    enabled = false; 
 
    fwdThrusters.ForEach(thruster => thruster.SetValue("Override", float.MinValue)); 
    bwdThrusters.ForEach(thruster => thruster.ApplyAction("OnOff_On")); 
 
    fwdThrusters.Clear(); 
    bwdThrusters.Clear(); 
    controllers.Clear(); 
} 
 
private void Update() 
{ 
    if (!enabled) return; 
 
    // Ensure approximately updateRate updates per second 
    time++; 
    if (time < 60 / period) return; 
    time = 0; 
 
    // Ensure that at least one ship controller is being controlled by a player 
    IMyShipController reference = getActiveController(); 
    if (reference == null) 
    { 
        Disable(); 

        // get all parachutes and deploy them 
        List<IMyParachute> parachutes = new List<IMyParachute>(); 
        GridTerminalSystem.GetBlocksOfType<IMyParachute>(parachutes); 
        parachutes.ForEach(parachute => parachute.OpenDoor());

        return; 
    } 
 
    // Get total available thrust from forward thrusters 
    float availableThrust = fwdThrusters 
        .Where(thruster => thruster.IsWorking) 
        .Sum(thruster => thruster.MaxThrust); 
 
    // Get direction of gravity as well as forward direction of ship 
    Vector3D gravDir = reference.GetNaturalGravity(); 
    Vector3D shipDir = reference.WorldMatrix.Forward; 
 
    // if ship is pointing toward the ground, no thrust is needed to counter gravity 
    double dotProduct = Vector3D.Dot(gravDir, shipDir); 
    double gravComponent = -dotProduct / shipDir.Length(); 
 
    // find out how much speed we're missing 
    double missingSpeed = targetSpeed - reference.GetShipSpeed(); 
    double mass = reference.CalculateShipMass().TotalMass; 
    double maxAccel = availableThrust / mass / updateRate; 
    if (missingSpeed + gravComponent > maxAccel) 
    { 
        fwdThrusters.ForEach(thruster => thruster.SetValue("Override", float.MaxValue)); 
    } 
    else 
    { 
        double ratio = (missingSpeed + gravComponent) / maxAccel; 
        if (ratio > 0) Echo(ratio.ToString()); 
        fwdThrusters.ForEach(thruster => 
        { 
            thruster.SetValue("Override", (float)(ratio * thruster.MaxThrust)); 
        }); 
    } 
}
