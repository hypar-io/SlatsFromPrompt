using Elements;
using Elements.Geometry;
using Elements.Geometry.Solids;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using static System.Net.WebRequestMethods;

namespace ImageFromPrompt
{

    public class Response
    {
        public List<ResponseData> data { get; set; }
    }

    public class ResponseData
    {
        public string b64_json { get; set; }
        public string url { get; set; }
    }
    public class RequestBody
    {
        public string prompt { get; set; }
        public int n { get; set; }
        public string size { get; set; }
        public string response_format { get; set; } // b64_json or url
    }
    public static class ImageFromPrompt
    {
        /// <summary>
        /// The ImageFromPrompt function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A ImageFromPromptOutputs instance containing computed results and the model with any new elements.</returns>
        public static ImageFromPromptOutputs Execute(Dictionary<string, Model> inputModels, ImageFromPromptInputs input)
        {
            var output = new ImageFromPromptOutputs();

            if (input.Prompt.Length == 0)
            {
                output.Warnings.Add("Please set a prompt.");
                return output;
            }

            var apiKey = System.IO.File.ReadAllText("API_Key.txt");

            var request = (HttpWebRequest)WebRequest.Create("https://api.openai.com/v1/images/generations");
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            // set up the request body
            var requestBody = JsonConvert.SerializeObject(new
            {
                prompt = input.Prompt,
                n = 1,
                size = "512x512"
            });
            var data = System.Text.Encoding.ASCII.GetBytes(requestBody);
            request.ContentLength = data.Length;

            using (var stream = request.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }

            var response = request.GetResponse();
            var responseStream = response.GetResponseStream();
            var reader = new StreamReader(responseStream);
            var responseString = reader.ReadToEnd();

            // Parse out the data:
            var responseObj = JsonConvert.DeserializeObject<Response>(responseString);

            // Download the image and save it to the temp folder
            var imageUrl = responseObj.data[0].url;
            var imageBytes = new WebClient().DownloadData(imageUrl);
            // read image from file 
            var img = Image.Load(imageBytes);
            var tempPath = Path.GetTempPath();
            var imagePath = Path.Combine(tempPath, "image.png");
            System.IO.File.WriteAllBytes(imagePath, imageBytes);


            var texMat = new Material()
            {
                Color = Colors.White,
                Unlit = true,
                Texture = imagePath
            };
            static (Vector3 position, Vector3 normal, UV uv, Elements.Geometry.Color? color) ModifyVertexAttributes((Vector3 position, Vector3 normal, UV uv, Elements.Geometry.Color? color) vertex)
            {
                var (position, normal, uv, color) = vertex;
                var newUV = new UV(uv.U, 1 - uv.V);
                return (position, normal, newUV, color);
            }
            var xyScaleTransform = new Transform().Scaled(new Vector3(input.XDimension, input.YDimension, 1));
            var imgDisplay = new GeometricElement()
            {
                Representation = new Lamina(Polygon.Rectangle((0, 0), (1, 1))),
                Transform = xyScaleTransform,
                Material = texMat,
                ModifyVertexAttributes = ModifyVertexAttributes
            };
            output.Model.AddElement(imgDisplay);

            var rows = (double)input.SlatCount;
            var columns = (double)input.SlatResolution;

            for (int i = 0; i <= rows; i++)
            {
                var yPos = i / rows;
                var pts = new List<Vector3> {
                    xyScaleTransform.OfPoint(new Vector3(1, yPos, 0)),
                    xyScaleTransform.OfPoint(new Vector3(0, yPos, 0))
                };
                for (int j = 0; j <= columns; j++)
                {
                    var xPos = j / columns;
                    var pixel = img[(int)(xPos * (512 - 1)), (int)(yPos * (512 - 1))];
                    var col = pixel.R;
                    var z = (input.Invert ? 1.0 - (col / 255.0) : (col / 255.0)) * input.ZDimension + 0.5;
                    var pos = xyScaleTransform.OfPoint(new Vector3(xPos, yPos, z));
                    pts.Add(pos);
                }
                var pl = new Polygon(pts);
                output.Model.AddElement(new GeometricElement()
                {
                    Representation = new Representation
                    {
                        SolidOperations = new List<SolidOperation> {
                            new Extrude(pl, 0.1, Vector3.YAxis, false),
                       },
                        SkipCSGUnion = true
                    },
                    Name = $"Extrusion {i}",
                });
            }

            return output;
        }
    }
}