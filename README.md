# ScreenSpaceOutlines
This is an expansion of Robin Seibold's implementation of screen space outlines to better fit the requirements of our game design class digital protoype.

Here are the reference links to their implementation:
- https://www.youtube.com/watch?v=LMqio9NsqmM&ab_channel=RobinSeibold
- https://github.com/Robinseibold/Unity-URP-Outlines

Modifications:

Non-maximum supression (NSM) to resolve very steep (view-normal) angle transitions:

Here is an example of the artifact when there is no NSM. The slider is in the bottom left.
![image](https://github.com/EmmyVoita/Unity-URP-ScreenSpaceOutlines/assets/82542924/1e73f135-3122-498a-afeb-43824f298d85)

Here is an example of the of just the NSM output. Circled in blue, artifacts occur do to the offset when dealing with fine details. 

![image](https://github.com/EmmyVoita/Unity-URP-ScreenSpaceOutlines/assets/82542924/89dd48c3-e697-435d-8507-7bda0dc2b7e7)

Since the target issue occurs when the view dot normal is specifically close to 0, I blend between the NSM output and base output based on the dot product and the slider variable to allow for more control.  You can see that the artifiact around the sword detials is lessened, although not gone, so there is room for improvement. These screenshots are taken with 2x MSAA.
![image](https://github.com/EmmyVoita/Unity-URP-ScreenSpaceOutlines/assets/82542924/dbbfaa97-869b-46f9-8f10-09774f83b3fa)

Rendering Outlines as a pre-pass to the Unity render opaque geometry pass:

Rendering the outline layer as a prepass allows for other geometry to be easily rendered on top of outlines without requiring depth information. Then the outline layer is not rendered in the opaque geometry pass.
![image](https://github.com/EmmyVoita/Unity-URP-ScreenSpaceOutlines/assets/82542924/279cb0cf-9f90-4920-a022-9886b0ac931f)
