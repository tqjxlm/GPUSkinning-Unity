# GPUSkinning-Unity

Unity implementation of GPU skinning (animation instancing).
Targeting on mobile devives (es3) specifically, but is compatible with other platform.
Developed on Unity2019.1.

## Package Release Usage

1. Create a new 3D project in Unity Hub
2. Switch platform to Android in Build Settings
3. Switch quality level to Medium in Project Settings/Quality. Modify shadow distance to a larger value as you need
4. Import GPUSkinning.unitypackage via Asset/Import Package/Custom Package
5. You will encounter a compile error. If you don't need MeshSimplifier provided by this project, just delete /Assets/Editor/MeshSimplifier.cs. Otherwise, please add "com.whinarn.unitymeshsimplifier": "https://github.com/Whinarn/UnityMeshSimplifier.git" to <Project Directory>/Packages/manifest.json
6. Open /Assets/Scenes/ExampleScene and play the game

## Game Control

1. The basic control is similar to PUBG Mobile. Use the left stick to navigate. Slide anywhere else on the screen to view.
2. Some UI toggles are on the screen for controlling the game behaviour.

## Package Usage

The "Crowd" object in the example scene is the core component that matters, you can begin exploring the project from here

### How to change character meshes

* Expand the Crowd object and you will see 3 LODs. You can change the mesh of each of them.
* All meshes should share the same skeleton as specified in the Animator component of the Crowd object
* If your mesh has a different skeleton or avatar, please retarget it. Otherwise you should [generate a new prefab](#how-to-generate-a-new-prefab)

### How to change weapon meshes

* In the detail panel of the Crowd object, change the mesh list in the WeaponManager component
* The weapon will be bound to the bone specified in GPUSkinRenderer component

### How to bake a new animation

* In the detail panel of the Crowd object, change the animation list in the CrowdManager component
* Right click on the object and choose GPUSkinning/Bake Animation
* All animations should share the same skeleton as specified in the Animator component of the Crowd object
* If you want to use a new skeleton or avatar, please retarget your animations. A better way is to [generate a new prefab](#how-to-generate-a-new-prefab)

### How to generate a new prefab

1. Place your object with SkinnedMeshRenderer in the scene (if it is a prefab, please unpack it first)
2. If you want to use the weapon system, please remove the built-in weapon
3. Right click on the object and choose GPUSkinning/Generate Prefab
4. In the detail panel
   1. Specify a Controller and an Avatar to Animator
   2. Adjust the size of Capsule Collider if needed
   3. Specify a list of animations to CrowdManager
   4. Specify a list of LODs to GPUSkinRenderer. Every LOD should be assigned with an LOD child under the object
5. Add a behaviour script to control the crowd behaviour. The implementation used in demo is CrowdBehaviour.cs. You can refer to it to design your own
6. If you want to use the weapon system, specify a list of weapon meshes and a weapon material to WeaponManager, and specify a weapon bone to GPUSkinRenderer
7. Right click on the object and choose GPUSkinning/Bake Animation
