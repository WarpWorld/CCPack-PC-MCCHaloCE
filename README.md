# Halo Crowd Control Effect Pack

This is an effect pack for doing Crowd Control on Halo CE on the Master Chief Collection. This is a version of the Cursed Halo crowd control pack with a few incentives removed and some modified to have no elements from Cursed Halo.

## Installation
Requires: 
- Master Chief Collection on Steam. If you have it elsewhere, like Game Pass, look into how to install Steam Worshop mods on that version and it should work as well.
- And if trying to run it locally, the CrowdControl SDK. https://developer.crowdcontrol.live/sdk/
   - If not running locally, search Halo in the CrowdControl application.

## How it works

This effect pack works through code injection. It uses three kinds of patterns:
1. Injecting code on game instructions that are run once every game frame (or few frames) and access interesting addresses, like the player data base address, and storing those addresses on custom memory caves that we store a pointer for. That way, we can have a constantly updated pointer to info we may want to read or modify, like health and shields.
2. Injecting code that hijacks the behaviour of instructions, like hijacking the damage received function to multiply the damage value by a factor to increase or decrease it.
3. Communicating with H1 scripts. For many effects, this pack uses custom H1 (Halo CE scripting language) scripts, but H1 has no way of communicating with another process or read any external files. To allow the effect pack to communicate with the scripts, I define a global variable with a specific value in the H1 script, surrounded by two other globals with specific values, and then, using the patter 1, hook into the code that reads script variables and look for those three variables, saving a pointer to the first one. Then the pack uses the pointer to change that variable, which the script reads every frame to determine what it should run.

I have already attempted to do simpler injection using pointermaps, but it does not seem to work on Halo CE.
Also, every injection is based on the base address of halo1.dll, which is loaded inside the MCC. If it is not loaded, the pack will wait until it is.

## Project structure
- The entry point is MCCHaloCE.cs. The //ccpragma in the first line includes all the files into the main file so it can be loaded as an effect pack. It also contains the base elements of Crowd Control.
- LifeCycle contains code that makes sure the pack properly connects to the game, sets everything up, and repairs any problem, like a game crash or exiting to the main menu (which overwrites injections).
- Injections contains all the code that injects assembler. In each injection, the replaced code is copied so that if an update breaks the injections, it is easier to find.
- Effects contains implementations for specific effects.
- Utilities contains methods used by all the other code.
- HaloFiles contains the H1 script code relevant to the effect pack. Note that to modify this it needs to be added on each level of the Cursed Halo source code and rebuild such levels.

## Contributing

If you want to update broken injections or add new effects, feel free to create a pull request.

## Credits
Pack created by ZePistachio
CrowdControl framework is provided by CrowdControl

## License

[MIT](https://choosealicense.com/licenses/mit/)