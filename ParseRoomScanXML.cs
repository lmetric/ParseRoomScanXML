using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Text;
using System.Drawing;

namespace RoomScanAPI
{
    public class ParseRoomScanXML
    {
        public ParseRoomScanXML()
        {
        }

        /// <summary>
        /// This sample demonstrates reading in a RoomScan LiDAR XML output and parsing it
        /// </summary>
        public static void Create(string xml)
        {
            var doc = XDocument.Parse(xml);
            var project = doc.XPathSelectElement("/project");
            string buildingName = project.XPathSelectElement("name").Value;

            Console.WriteLine("Building name " + buildingName);

            var floors = doc.XPathSelectElements("/project/floors/floor");
            float floorLevel = 0;

            foreach (var floor in floors)
            {
                string floorName = floor.XPathSelectElement("name").Value;
                Console.WriteLine("Storey " + floorName + " at level " + floorLevel);

                var designs = floor.XPathSelectElements("designs/design");

                // usually there will just be one 'design' (floor plan) per floor, but with outbuildings etc there can be more
                foreach (var design in designs)
                {
                    var objects = design.XPathSelectElements("objects/object");

                    var areas = design.XPathSelectElements("areas/area");
                    var lines = design.XPathSelectElements("lines/line");

                    float floorHeight = 0.0F;

                    foreach (var area in areas)
                    {
                        if (area.Attribute("type").Value != "room")
                        {
                            continue;
                        }

                        string roomName = area.XPathSelectElement("name").Value;
                        string roomIndex = area.Attribute("id").Value;
                        string roomColourString = area.XPathSelectElement("color").Value;
                        Color roomColour = System.Drawing.ColorTranslator.FromHtml(roomColourString);
                        Console.WriteLine("Room name " + roomName + " colour " + roomColourString);

                        string pointsS = area.XPathSelectElement("points").Value;
                        List<(float, float)> pointsForArea = new List<(float, float)>();
                        foreach (string line in pointsS.Split(','))
                        {
                            float[] points = Array.ConvertAll(line.Split(',')[0].Split(' '), s => float.Parse(s));
                            pointsForArea.Add((points[0], -points[1]));
                        }

                        string heightS = area.XPathSelectElement("height").Value;
                        float height = float.Parse(heightS);
                        if (height > floorHeight) { floorHeight = height; }

                        Console.WriteLine("Points for floor area calculation: " + string.Join(", ", pointsForArea.ToArray()));

                        var linesInRoom = lines.Where(l => l.Attribute("area-id").Value == area.Attribute("id").Value && l.XPathSelectElement("type").Value == "simple_wall");
                        var objectsInRoom = objects.Where(o => o.XPathSelectElement("position/rooms").Value.Split(',')[0] == roomIndex);

                        foreach (var lineElement in linesInRoom)
                        {
                            string line = lineElement.XPathSelectElement("points").Value;
                            string wallIndex = lineElement.Attribute("id").Value;
                            float[] points = Array.ConvertAll(line.Split(',')[0].Split(' '), s => float.Parse(s));
                            float x1 = points[0];
                            float y1 = -points[1];
                            float x2 = points[3];
                            float y2 = -points[4];

                            float wallLength = (float)Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));
                            Console.WriteLine("Wall length {0} from {1},{2} to {3},{4} height {5}", wallLength, x1, y1, x2, y2, height);

                            var objectsOnWall = objectsInRoom.Where(o => o.XPathSelectElement("position/wall") != null && o.XPathSelectElement("position/wall").Value == wallIndex);

                            foreach (var o in objectsOnWall)
                            {
                                float along = float.Parse(o.XPathSelectElement("position/along").Value);
                                float[] oPoints = Array.ConvertAll(o.XPathSelectElement("points").Value.Split(' '), s => float.Parse(s));
                                float[] oSize = Array.ConvertAll(o.XPathSelectElement("size").Value.Split(' '), s => float.Parse(s));
                                float[] oRotation = Array.ConvertAll(o.XPathSelectElement("rotation").Value.Split(' '), s => float.Parse(s));

                                // cx,cy is the centre of the opening
                                float cx = x2 * along + x1 * (1.0F - along);
                                float cy = y2 * along + y1 * (1.0F - along);

                                // dx,dy is the centre of the door itself, which may be set back into the opening
                                float dx = oPoints[0];
                                float dy = -oPoints[1];

                                float oWidth = oSize[0];
                                float oSill = oPoints[2];
                                float oHeight = oSize[2];
                                float rotation = (float)(-oRotation[2] * Math.PI / 180.0);

                                float ox1 = (float)(cx + oWidth * Math.Cos(rotation));
                                float oy1 = (float)(cy + oWidth * Math.Sin(rotation));
                                float ox2 = (float)(cx - oWidth * Math.Cos(rotation));
                                float oy2 = (float)(cy - oWidth * Math.Sin(rotation));
                                float perpAngle = (float)(Math.Atan2(y2 - y1, x2 - x1));

                                // auxiliary if true means that this is a door that was added in the adjacent room
                                // but which connects to this one -- it's a copy of the original to make it easy to
                                // determine that a gap is needed in the wall in this room without having to go
                                // searching in adjacent rooms
                                bool auxiliary = (o.XPathSelectElement("auxiliary").Value == "yes");
                                string type = o.XPathSelectElement("type").Value;

                                if (auxiliary || type == "opening")
                                {
                                    Console.WriteLine("Opening width {0} at {1},{2} to {3},{4} sill height {5} lintel height {6}", oWidth, ox1, oy1, ox2, oy2, oSill, oHeight);
                                }
                                else if (type == "door" || type == "double-sliding" || type == "single-sliding" || type == "garage-door" || type == "single-folding" || type == "double-folding")
                                {
                                    string[] roomList = o.XPathSelectElement("position/rooms").Value.Split(',');
                                    string destination = "exterior";
                                    if (roomList.Count() == 2)
                                    {
                                        string otherRoomIndex = roomList[1];
                                        var otherRoom = areas.Where(a => a.Attribute("id") != null && a.Attribute("id").Value == otherRoomIndex);
                                        if (otherRoom != null && otherRoom.Count() > 0)
                                        {
                                            destination = otherRoom.First().XPathSelectElement("name").Value;
                                        }
                                    }

                                    Console.WriteLine("Door to {7} of type {8} width {0} at {1},{2} to {3},{4} sill height {5} lintel height {6}",
                                        oWidth, ox1, oy1, ox2, oy2, oSill, oHeight, destination, type);
                                }
                                else if (type == "window")
                                {
                                    Console.WriteLine("Window width {0} at {1},{2} to {3},{4} sill height {5} lintel height {6}", oWidth, ox1, oy1, ox2, oy2, oSill, oHeight);
                                }
                            }
                        }
                    }

                    floorLevel -= floorHeight;
                }
            }
        }
    }
}
