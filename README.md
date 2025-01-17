# ScreenSpaceOutlines
This project is an enhancement of Robin Seibold's screen space outlines implementation, which I expanded upon to meet the requirements of various projects I was working on. These enhancements included improving edge detection using non-maximum suppression and adding anti-aliasing. I initially worked on this project using Unity version 2022.3.50f1, but I encountered issues with setting multiple render targets, which I needed for implementing a Temporal Anti-Aliasing (TAA) shader. To diagnose bugs, I relied heavily on RenderDoc and eventually decided to switch to Unity 6.0 to use the Render Graph system.

**Non-maximum supression (NSM) to resolve very steep (view-normal) angle transitions:**

Here is an example of the artifact when there is no NMS applied (left), and the issue resolved with NMS applied (right).

![NMS_1](https://github.com/user-attachments/assets/09fd89b1-6e21-420b-8960-80e9d45cd1cd)


If you are interested you can read more about the project here:


**Links**
- https://www.youtube.com/watch?v=LMqio9NsqmM&ab_channel=RobinSeibold
- https://github.com/Robinseibold/Unity-URP-Outlines
