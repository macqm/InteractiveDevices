﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Services;
using MOIS;
using Origami.Modules;
using Origami.States;
using Origami.Utilities;
using Kinect = Microsoft.Kinect;
using Mogre;
using System.IO;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using System.Drawing;
using System.Runtime.InteropServices;
using Vector3 = Mogre.Vector3;

namespace Origami
{
    public class Program
    {

        private const int CV_WND_PROP_FULLSCREEN = 0;
        public const string OPENCV_HIGHGUI_LIBRARY = "opencv_highgui240";
        public const UnmanagedType StringMarshalType = UnmanagedType.LPStr;
        private const int CV_WINDOW_FULLSCREEN = 1;


        [DllImport(OPENCV_HIGHGUI_LIBRARY, CallingConvention = CvInvoke.CvCallingConvention, EntryPoint = "cvMoveWindow")]
        private static extern void moveWindow([MarshalAs(StringMarshalType)] String name, int X, int y);

        [DllImport(OPENCV_HIGHGUI_LIBRARY, CallingConvention = CvInvoke.CvCallingConvention, EntryPoint = "cvSetWindowProperty")]
        private static extern void _cvSetWindowProperty([MarshalAs(StringMarshalType)] String name, int prop, double propvalue);

        [DllImport(OPENCV_HIGHGUI_LIBRARY, CallingConvention = CvInvoke.CvCallingConvention, EntryPoint = "cvNamedWindow")]
        private static extern void _cvNamedWindow([MarshalAs(StringMarshalType)] String name, int prop);

        /// <summary>
        /// Creates a window which can be used as a placeholder for images and trackbars. Created windows are reffered by their names. 
        /// If the window with such a name already exists, the function does nothing.
        /// </summary>
        /// <param name="name">Name of the window which is used as window identifier and appears in the window caption</param>
        public static void cvSetWindowProperty(String name)
        {
            //return _cvSetWindowProperty(name, CV_WND_PROP_FULLSCREEN, CV_WINDOW_FULLSCREEN);
            _cvSetWindowProperty(name, 0, 1);
        }

        //////////////////////////////////////////////////////////////////////////
        private static OgreManager mEngine;
        private static StateManager mStateMgr;

        //////////////////////////////////////////////////////////////////////////
        private Light mLight1;
        private Light mLight2;

        ////** KINECT STUFF **
        private readonly Kinect.KinectSensor sensor;
        private readonly byte[] colorPixels;

        /// <summary>
        /// Paper position, width and height detected by Kinect
        /// </summary>
        private SceneNode cSceneNode;

        private readonly Kinect.DepthImagePixel[] depthPixes;

        private const string KinectColorWindowName = "Kinect color";
        private const string KinectDepthWindowName = "Kinect depth";
        private const string KinectThresholdWindowName = "Threshold window";

        private Kinect.SkeletonPoint skeletonPoint;
        private Matrix4 projectionMatrix;
        private Matrix4 viewMatrix;
        private Matrix4 transformMat;
        private Matrix4 inverseTransformMat;
        private readonly IList<Kinect.SkeletonPoint> skeletonPoints = new List<Kinect.SkeletonPoint>();
        private ManualObject origamiMesh;
        private Kinect.SkeletonPoint centralPoint;
        private static Vector3 centralPointProj;
        private static Vector3 normal;


        private readonly List<string> bookMaterials;
        private int currentMaterialIndex = 0;
        private bool textureFlip = false;

        /************************************************************************/
        /* program starts here                                                  */
        /************************************************************************/
        [STAThread]
        static void Main()
        {
            // create Ogre manager
            mEngine = new OgreManager();

            // create state manager
            mStateMgr = new StateManager(mEngine);

            // create main program
            var prg = new Program();
           

            // try to initialize Ogre and the state manager
            if (mEngine.Startup() && mStateMgr.Startup(typeof(TurningHead)))
            {
                mEngine.Keyboard.KeyPressed += prg.Keyboard_KeyPressed;
                
                
                // create objects in scene
                prg.CreateScene();

                // run engine main loop until the window is closed
                while (!mEngine.Window.IsClosed)
                {
                    // update the objects in the scene
                    prg.UpdateScene();

                    // update Ogre and render the current frame
                    mEngine.Update();
                }

                // remove objects from scene
                prg.RemoveScene();
            }

            // shut down state manager
            mStateMgr.Shutdown();

            // shutdown Ogre
            mEngine.Shutdown();
        }

        private bool Keyboard_KeyPressed(MOIS.KeyEvent arg)
        {
            // Key press
            switch (arg.key)
            {
                case KeyCode.KC_1:
                    this.currentMaterialIndex = 0;
                    break;
                case KeyCode.KC_2:
                    currentMaterialIndex = 1;
                    break;
                case KeyCode.KC_3:
                    currentMaterialIndex = 2;
                    break;
                case KeyCode.KC_4:
                    currentMaterialIndex = 3;
                    break;
                case KeyCode.KC_5:
                    currentMaterialIndex = 4;
                    break;
                case KeyCode.KC_F:
                    textureFlip = !textureFlip;
                    break;
                case KeyCode.KC_LEFT:
                    ShiftX -= 0.01f;
                    break;
                case KeyCode.KC_RIGHT:
                    ShiftX += 0.01f;
                    break;
                case KeyCode.KC_UP:
                    ShiftY -= 0.01f;
                    break;

                case KeyCode.KC_DOWN:
                    ShiftY += 0.01f;
                    break;
            }
            
            origamiMesh.SetMaterialName(0, this.bookMaterials[currentMaterialIndex]);
            return true;
        }

        /************************************************************************/
        /* constructor                                                          */
        /************************************************************************/
        public Program()
        {

            ShiftX = -0.09f;
            ShiftY = -0.14f;
            this.ShiftZ = 0;


            bookMaterials = new List<string>
            {
                "book1_material",
                "book2_material",
                "book3_material",
                "book4_material",
                "book5_material"
            };

            mLight1 = null;
            mLight2 = null;

            foreach (var potentialSensor in Kinect.KinectSensor.KinectSensors)
            {
                if (potentialSensor.Status == Kinect.KinectStatus.Connected)
                {
                    this.sensor = potentialSensor;
                    break;
                }
            }


            if (null != this.sensor)
            {
                this.sensor.ColorStream.Enable(Kinect.ColorImageFormat.RgbResolution640x480Fps30);
                this.sensor.DepthStream.Enable(Kinect.DepthImageFormat.Resolution640x480Fps30);

                // Initialize buffer for pixels from kinect
                this.colorPixels = new byte[this.sensor.ColorStream.FramePixelDataLength];
                this.depthPixes = new Kinect.DepthImagePixel[this.sensor.DepthStream.FramePixelDataLength];
                this.sensor.AllFramesReady += sensor_AllFramesReady;

                CvInvoke.cvNamedWindow(KinectColorWindowName);
                CvInvoke.cvNamedWindow(KinectDepthWindowName);
                _cvNamedWindow(KinectThresholdWindowName, 0x00000100);

                //moveWindow(KinectThresholdWindowName, 2161, 0);

                //cvSetWindowProperty(KinectThresholdWindowName);

                Console.WriteLine("RET: {0}");

                try
                {
                    this.sensor.Start();
                }
                catch (IOException)
                {
                    this.sensor = null;
                }
            }
        }

        void sensor_AllFramesReady(object sender, Kinect.AllFramesReadyEventArgs e)
        {
            using (var colorFrame = e.OpenColorImageFrame())
            {
                if (colorFrame == null)
                {
                    return;
                }

                using (var depthFrame = e.OpenDepthImageFrame())
                {
                    if (depthFrame == null)
                    {
                        return;
                    }             
                    colorFrame.CopyPixelDataTo(this.colorPixels);
                    depthFrame.CopyDepthImagePixelDataTo(this.depthPixes);

                    var handle = GCHandle.Alloc(this.colorPixels, GCHandleType.Pinned);
                    var image = new Bitmap(colorFrame.Width,
                        colorFrame.Height,
                        colorFrame.Width << 2, System.Drawing.Imaging.PixelFormat.Format32bppRgb,
                        handle.AddrOfPinnedObject());
                    handle.Free();

                    var handle2 = GCHandle.Alloc(this.depthPixes, GCHandleType.Pinned);
                    var image2 = new Bitmap(depthFrame.Width,
                        depthFrame.Height,
                        depthFrame.Width << 2, System.Drawing.Imaging.PixelFormat.Format32bppRgb,
                        handle2.AddrOfPinnedObject());
                    handle2.Free();

                    var colorImage = new Image<Bgr, byte>(image);
                    var openCvImgGrayscale = new Image<Gray, byte>(image);
                    var depthImage = new Image<Bgr, byte>(image2);
                    image.Dispose();

                    //  Get points form depth sensor
                    var depthImagePoints = new Kinect.DepthImagePoint[colorFrame.Width * colorFrame.Height];

                    // Map color and depth frame from Kinect
                    var mapper = new Kinect.CoordinateMapper(sensor);
                    mapper.MapColorFrameToDepthFrame(colorFrame.Format, depthFrame.Format, depthPixes,
                        depthImagePoints);


                    // Get threshold value
                    const int thresholdMin = 140;
                    const int thresholdMax = 255;

                    // Thresholding
                    var trimmedColorImage = ExtractSubSection(colorImage);

                    var thresholdImage = openCvImgGrayscale.ThresholdBinary(new Gray(thresholdMin), new Gray(thresholdMax));

                    var trimmedthresholdImage = ExtractSubSection(thresholdImage);


                    thresholdImage.SmoothMedian(3);


                    var testWindowContent = new Image<Bgr, byte>(trimmedColorImage.Size);

                    var centre = new Point();
                    var points = FindContours(trimmedthresholdImage, testWindowContent, ref centre);



                    // Find homography
                    lock (this.skeletonPoints)
                    {
                        this.skeletonPoints.Clear();
                    }

                    //testWindowContent;

               

                    foreach (var point in points)
                    {
                        // Find where the X,Y point is in the 1-D array of color frame
                        var index = point.Y * colorFrame.Width + point.X;

                        // Let's choose point e.g. (x, y)
                        trimmedColorImage.Draw(new Cross2DF(
                            new PointF(point.X, point.Y), 2.0f, 2.0f), new Bgr(Color.Red), 5);

                        // Draw it on depth image
                        depthImage.Draw(new Cross2DF(
                            new PointF(depthImagePoints[index].X, depthImagePoints[index].Y),
                            2.0f, 2.0f), new Bgr(Color.White), 1);
                        
                        

                        // Get the point in skeleton space
                        var sp = mapper.MapDepthPointToSkeletonPoint(
                            depthFrame.Format,
                            depthImagePoints[index]);

                        lock (this.skeletonPoints)
                        {

                            this.skeletonPoints.Add(sp);
                        }


                    }

                    // Find centre
                    // Find where the X,Y point is in the 1-D array of color frame
                    var cIndex = centre.Y * colorFrame.Width + centre.X;
                    // Let's choose point e.g. (x, y)
                    trimmedColorImage.Draw(new Cross2DF(
                        new PointF(centre.X, centre.Y), 2.0f, 2.0f), new Bgr(Color.Red), 5);

                        // Draw it on depth image
                    if (cIndex < depthImagePoints.Count())
                    {
                        depthImage.Draw(new Cross2DF(
                            new PointF(depthImagePoints[cIndex].X, depthImagePoints[cIndex].Y),
                            2.0f, 2.0f), new Bgr(Color.White), 1);

                        // Get the point in skeleton space

                        this.centralPoint = mapper.MapDepthPointToSkeletonPoint(
                            depthFrame.Format,
                            depthImagePoints[cIndex]);
                    }

                    this.skeletonPoint = this.skeletonPoints.FirstOrDefault();

                    CvInvoke.cvShowImage(KinectColorWindowName, trimmedColorImage);
                    CvInvoke.cvShowImage(KinectDepthWindowName, depthImage);
                    CvInvoke.cvShowImage(KinectThresholdWindowName, trimmedthresholdImage);
                }
            }
        }

        /// <summary>
        /// Transform kinect point from skeleton to scene space (use transform matrix)
        /// </summary>
        /// <param name="kinectPoint"></param>
        /// <returns></returns>
        public Vector3 ConvertKinectToProjector(Vector3 kinectPoint)
        {
            // Transform by transformation matrix
            var pointTranlated = transformMat*kinectPoint;
            return pointTranlated;
        }

        private static Image<TColor, TDepth> ExtractSubSection<TColor, TDepth>(Image<TColor, TDepth> sourceImage)
            where TColor : struct, IColor
            where TDepth : new()
        {
            // TODO: Read from config file
            int paddingTop = Config.Instance.Padding.top;
            int paddingBottom = Config.Instance.Padding.bottom;

            int paddingLeft = Config.Instance.Padding.left;
            int paddingRight = Config.Instance.Padding.right;

            var maskImage = new Image<TColor, TDepth>(sourceImage.Size);

            // Set to black
            maskImage.SetZero();

            // Copy values to the black mask
            for (var row = paddingTop; row < sourceImage.Height - paddingBottom; row++)
            {
                for (var col = paddingLeft; col < sourceImage.Width - paddingRight; col++)
                {
                    maskImage[row, col] = sourceImage[row, col];
                }
            }

            return maskImage;
        }

        private static IEnumerable<Point> FindContours(Image<Gray, byte> thresholdImage, 
            Image<Bgr, byte> testWindowContent, ref Point centre)
        {
            var points = new List<Point>();

            using (var storage = new MemStorage())
            {
                // Find contours
                var contours = thresholdImage.FindContours(
                    CHAIN_APPROX_METHOD.CV_CHAIN_APPROX_NONE,
                    RETR_TYPE.CV_RETR_TREE,
                    storage);

                if (contours != null)
                {
                    var polygonPoints = contours.ApproxPoly(12.0);

                    var moments = new MCvMoments();
                    CvInvoke.cvMoments(contours, ref moments, 1);

                    try
                    {
                        centre = new Point((int) moments.m10/(int) moments.m00, (int) moments.m01/(int) moments.m00);
                    }
                    catch (DivideByZeroException)
                    {
                        centre = new Point(0,0);
                    }

                    testWindowContent.Draw(polygonPoints, new Bgr(Color.Yellow), 2);

                    points.AddRange(polygonPoints.Select(polygonPoint => new Point(polygonPoint.X, polygonPoint.Y)));

                    for (int i = 0; i < points.Count; i++)
                    {
                        Point pt = points[i];

                        float dX = 0.1f * (centre.X - pt.X);
                        float dY = 0.1f * (centre.Y - pt.Y);

                        points[i] = new Point(pt.X + (int)dX, pt.Y + (int)dY);
                    }
                }
            }

            return points;
        }


        /************************************************************************/
        /* create a scene to render                                             */
        /************************************************************************/
        public void CreateScene()
        {
            float distanceCameraProjection = Config.Instance.CameraDistance;
            float cameraAngelDeg = Config.Instance.CameraAngle;
            float heightCamera = Config.Instance.CameraHeight;

            mEngine.Window.GetViewport(0).BackgroundColour = new ColourValue(1f, 1f, 1f);

            // set a dark ambient light
            mEngine.SceneMgr.AmbientLight = new ColourValue(0.1f, 0.1f, 0.1f);

            // place the camera to a better position
            mEngine.Camera.Position = new Vector3(0.0f, 0.0f, -10.0f);
            mEngine.Camera.Direction = Vector3.UNIT_Z;
           
           // mEngine.Camera.LookAt(Vector3.UNIT_Z);

            InitializeViewAndProjectionMatrices();

            // Create transform matrix
            CreateTransformMatrix(heightCamera, distanceCameraProjection, cameraAngelDeg);

            this.inverseTransformMat = transformMat.Inverse();

            this.viewMatrix = this.viewMatrix * inverseTransformMat;
           
            // Apply our matrices to the camera
            mEngine.Camera.SetCustomProjectionMatrix(true, this.projectionMatrix);
            mEngine.Camera.SetCustomViewMatrix(true, this.viewMatrix);

            Console.WriteLine("Transform X={0} Y={1} Z={2}", this.viewMatrix[0,3],this.viewMatrix[1,3],this.viewMatrix[2,3]);
            
            // create one bright front light
            mLight1 = mEngine.SceneMgr.CreateLight("LIGHT1");
            mLight1.Type = Light.LightTypes.LT_POINT;
            mLight1.DiffuseColour = new ColourValue(1.0f, 0.975f, 0.85f);
            mLight1.Position = new Vector3(0f, 1f, 0f);
            mEngine.SceneMgr.RootSceneNode.AttachObject(mLight1);

            cSceneNode = mEngine.SceneMgr.RootSceneNode.CreateChildSceneNode();
            cSceneNode.SetPosition(0.0f, 0.0f, 0.0f);
            cSceneNode.Scale(new Vector3(1f, 1f, 1f));
            //cSceneNode.Rotate(new Vector3(1.0f, 0.0f, 0.0f), new Radian(new Degree(40)));

            origamiMesh = CreateMesh("Cube", this.bookMaterials.First());
            origamiMesh.CastShadows = false;
            cSceneNode.AttachObject(origamiMesh);

            //groundEnt.SetMaterialName("my1_myC");
            //groundEnt.CastShadows = false;
            //cSceneNode.AttachObject(groundEnt);


        }

        private void InitializeViewAndProjectionMatrices()
        {
            // Read from settings
            var calibrationReader = new CalibrationSettingsReader("device_0.txt");
            calibrationReader.Read();

            // Set Projection matrix
            this.projectionMatrix = calibrationReader.ProjectionMatrix;

            // View Matrix
            this.viewMatrix = calibrationReader.ViewMatrix;
        }

        private struct tex_t
        {
            public int u { get; set; }
            public int v { get; set; }
        }

        float ShiftX
        {
            get; set;
        }

        float ShiftY { get; set; } 
        float ShiftZ { get; set; }


        void UpdateMeshPoints(ManualObject mesh, IEnumerable<Vector3> points)
        {
            // Begin updating the mesh
            mesh.BeginUpdate(0);

            // Assign points
            var sortedPoints = points.ToList();

            var beforeSorting = points.ToList();

            // Calculate normal
            var threePoints = points.Take(3).ToArray();

            var ba = threePoints[1] - threePoints[0];
            var ca = threePoints[2] - threePoints[0];

            var dir = ba.CrossProduct(ca);
            dir.Normalise();
            Program.normal = dir.NormalisedCopy;

            sortedPoints.Sort(PointSorter);

            var text_coords = new List<tex_t>
            {
                new tex_t {u = 1, v = 1},
                new tex_t {u = 0, v = 1},
                new tex_t {u = 1, v = 0},
                new tex_t {u = 0, v = 0},
            };

            if (this.textureFlip)
            {
                var temp = text_coords[0];
                text_coords[0] = text_coords[3];
                text_coords[3] = temp;
            }

            int noTriangles = sortedPoints.Count - 2;

            // Limit it for now
            if (noTriangles > 2)
            {
                noTriangles = 2;
            }

            for (var t = 1; t < noTriangles + 1; t++)
            {
                // Odd Triangle
                if (t%2 == 0)
                {
                    // Vertex 0
                    var point = sortedPoints[t - 1];
                    var pt = new Vector3(point.x + this.ShiftX, point.y + this.ShiftY, point.z + this.ShiftZ);

                    mesh.Position(pt);
                    mesh.Normal(normal);
                    mesh.TextureCoord(text_coords[t - 1].u, text_coords[t-1].v);

                    // Vertex 1
                    point = sortedPoints[t];
                    pt = new Vector3(point.x + this.ShiftX, point.y + this.ShiftY, point.z + this.ShiftZ);

                    mesh.Position(pt);
                    mesh.Normal(normal);
                    mesh.TextureCoord(text_coords[t].u, text_coords[t].v);

                    // Vertex 2
                    point = sortedPoints[t + 1];
                    pt = new Vector3(point.x + this.ShiftX, point.y + this.ShiftY, point.z + this.ShiftZ);
                    mesh.Position(pt);
                    mesh.Normal(normal);
                    mesh.TextureCoord(text_coords[t + 1].u, text_coords[t + 1].v);
                    
                }
                    // Even triangle
                else
                {
                    // Vertex 2
                    var point = sortedPoints[t - 1];
                    var pt = new Vector3(point.x + this.ShiftX, point.y + this.ShiftY, point.z + this.ShiftZ);

                    mesh.Position(pt);
                    mesh.Normal(normal);
                    mesh.TextureCoord(text_coords[t - 1].u, text_coords[t - 1].v);

                    // Vertex 3
                    point = sortedPoints[t + 1];
                    pt = new Vector3(point.x + this.ShiftX, point.y + this.ShiftY, point.z + this.ShiftZ);

                    mesh.Position(pt);
                    mesh.Normal(normal);
                    mesh.TextureCoord(text_coords[t + 1].u, text_coords[t + 1].v);

                    // Vertex 1
                    point = sortedPoints[t];
                    pt = new Vector3(point.x + this.ShiftX, point.y + this.ShiftY, point.z + this.ShiftZ);
                    mesh.Position(pt);
                    mesh.Normal(normal);
                    mesh.TextureCoord(text_coords[t].u, text_coords[t].v);
                }
            }
            
            mesh.End();
        }

        private static bool IsLess(Vector3 a, Vector3 b)
        {

            if (a.x - centralPointProj.x >= 0 && b.x - centralPointProj.x < 0)
                return true;
            if (a.x - centralPointProj.x < 0 && b.x - centralPointProj.x >= 0)
                return false;
            if (a.x - centralPointProj.x == 0 && b.x - centralPointProj.x == 0)
            {
                if (a.y - centralPointProj.y >= 0 || b.y - centralPointProj.y >= 0)
                    return a.y > b.y;
                return b.y > a.y;
            }

            // compute the cross product of vectors (centralPointProj -> a) x (centralPointProj -> b)
            var det = (a.x - centralPointProj.x) * (b.y - centralPointProj.y) - (b.x - centralPointProj.x) * (a.y - centralPointProj.y);
            if (det < 0)
                return true;
            if (det > 0)
                return false;

            // points a and b are on the same line from the centralPointProj
            // check which point is closer to the centralPointProj
            var d1 = (a.x - centralPointProj.x) * (a.x - centralPointProj.x) + (a.y - centralPointProj.y) * (a.y - centralPointProj.y);
            var d2 = (b.x - centralPointProj.x) * (b.x - centralPointProj.x) + (b.y - centralPointProj.y) * (b.y - centralPointProj.y);
            return d1 > d2;
        }

        private static int PointSorter(Vector3 a, Vector3 b)
        {
            if (IsLess(a, b))
            {
                return -1;
            }
            else
            {
                return 1;
            }

            ////  Reference Point is Vector2.ZERO
            //var cm = centralPointProj;

            //var ac = a - cm;
            //var bc = b - cm;
            
            //ac.Normalise();
            //bc.Normalise();

            //var cross = ac.CrossProduct(bc);
            //var result = normal.DotProduct(cross);

            //if (result > 0.0f)
            //{
            //    return 1;
            //}
            //return -1;
        }

        ManualObject CreateMesh(string name, string matName)
        {

            var mesh = new ManualObject(name) {Dynamic = true};

            var initialPoints = new List<dynamic>
            {
                // Low left
                new {pos = new Vector3(-0.05864353f, -0.000508666f, 0.009420693f), col = ColourValue.Green},
                
                // Low right
                new {pos = new Vector3(0.1237954f, 0.01428729f, -0.03967981f), col = ColourValue.Red},
                
                // Upper left
                new {pos = new Vector3(-0.02506714f, -0.002875268f, 0.1396815f), col = ColourValue.Blue},
                
                // Upper right
                new {pos = new Vector3(0.1572229f, 0.007466197f, 0.08941139f), col = ColourValue.Green},
            };

            // OT_TRIANGLE_STRIP - 3 vertices for the first triangle and 1 per triangle after that
            mesh.Begin(matName, RenderOperation.OperationTypes.OT_TRIANGLE_LIST);

            mesh.Position(initialPoints[0].pos);
            mesh.TextureCoord(0, 0);
            //mesh.Colour(ColourValue.Red);
            
            mesh.Position(initialPoints[1].pos);
            mesh.TextureCoord(1, 0);
            //mesh.Colour(ColourValue.Red);


            mesh.Position(initialPoints[2].pos);
            mesh.TextureCoord(0, 1);
           // mesh.Colour(ColourValue.Red);


            mesh.Position(initialPoints[1].pos);
            mesh.TextureCoord(1, 0);
            //mesh.Colour(ColourValue.Red);


            mesh.Position(initialPoints[3].pos);
            mesh.TextureCoord(1, 1);
           // mesh.Colour(ColourValue.Red);



            mesh.Position(initialPoints[2].pos);
            mesh.TextureCoord(0, 1);
            //mesh.Colour(ColourValue.Red);

            
            
            ///,mesh.Triangle(0, 1, 2); 
            //cube.Triangle(3, 1, 0);
            
            mesh.End();

            return mesh;

        }

        /// <summary>
        /// Create transformation matrix
        /// </summary>
        /// <param name="cameraHeighInMeters">Camera height in meters</param>
        /// <param name="zTransl">Distance between origin on projection and camera</param>
        /// <param name="angleOfTheCameraInDeg">Camera angle</param>
        private void CreateTransformMatrix(
            float cameraHeighInMeters, 
            float zTransl,
            float angleOfTheCameraInDeg)
        {
            this.transformMat = new Matrix4();

                this.transformMat.MakeTransform(
                new Vector3(0, cameraHeighInMeters, zTransl),
                new Vector3(1, 1, 1),
                new Quaternion(new Radian(new Degree(angleOfTheCameraInDeg)), new Vector3(1, 0, 0)));
        }

        /************************************************************************/
        /* update objects in the scene                                          */
        /************************************************************************/

        public void UpdateScene()
        {
            mEngine.Keyboard.Capture();
            if (mEngine.Keyboard.IsKeyDown(KeyCode.KC_SPACE))
            {
                Console.Write("KEY DOWN\n");
            }

            var initialPoints = new List<Vector3>
            {
                new Vector3(-.5f, .5f, 0.324f),
                new Vector3(-.5f, -.5f, 0.323f),
                new Vector3(.5f, -.5f, 0.324f),
                new Vector3(.5f, .5f, 0.322f)
            };

            if (this.cSceneNode != null && this.skeletonPoints != null)
            {
                lock(this.skeletonPoints)
                {
                    var points = this.skeletonPoints.ToArray();
                

                    if (points.Any() && points.Count() >= 3)
                    {
                        var scenePoints = points.ToArray().
                            Select(kinectPoint => ConvertKinectToProjector(
                                new Vector3(-kinectPoint.X, kinectPoint.Y, kinectPoint.Z))).ToList();

                        Program.centralPointProj = ConvertKinectToProjector(new Vector3(-this.centralPoint.X, this.centralPoint.Y, this.centralPoint.Y));


                        UpdateMeshPoints(origamiMesh, scenePoints);

                        //var newPoint = ConvertKinectToProjector(new Vector3(-skeletonPoint.X, skeletonPoint.Y, skeletonPoint.Z));
                        //this.cSceneNode.SetPosition(newPoint.x, newPoint.y, newPoint.z);
                    }
                }
            }

            mStateMgr.Update(0);
        }

        /************************************************************************/
        /*                                                                      */
        /************************************************************************/
        public void RemoveScene()
        {
            // Shut down the kinect 
            if (sensor != null)
            {
                sensor.Stop();
            }

            // check if light 2 exists
            if (mLight2 != null)
            {
                // remove light 2 from scene and destroy it
                mEngine.SceneMgr.RootSceneNode.DetachObject(mLight2);
                mEngine.SceneMgr.DestroyLight(mLight2);
                mLight2 = null;
            }

            // check if light 1 exists
            if (mLight1 != null)
            {
                // remove light 1 from scene and destroy it
                mEngine.SceneMgr.RootSceneNode.DetachObject(mLight1);
                mEngine.SceneMgr.DestroyLight(mLight1);
                mLight1 = null;
            }
        }
    } // class

} // namespace
