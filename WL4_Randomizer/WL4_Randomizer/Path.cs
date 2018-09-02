﻿using System;
using System.Collections.Generic;
using System.IO;

namespace WL4_Randomizer
{
    class PathCreator
    {
        public const int DoorTableLocation = 0x78F21C, LevelHeadersLocation = 0x639068, LevelIndexLocation = 0x6391C4;

        public RoomNode[] rooms;
        public List<PathNode> path;

        public PathCreator(ref byte[] romReference, byte hall, byte level, ref string[] file, int fileOffset)
        {
            path = new List<PathNode>();

            // Get the start of the level header
            int doorTableLocation = LevelIndexLocation + hall * 24 + level * 4;
            doorTableLocation = Program.GetPointer(DoorTableLocation + romReference[doorTableLocation] * 4);

            int romIndex;
            int index = 0;
            byte roomMax = 0;
            List<byte> roomDoorIsIn = new List<byte>();
            PathType type = PathType.Door;

            // Door type, room, (size) Left, right, top, bottom, door to link to, offsetX, offsetY, ???, ???, ???

            // Reading rom for doorways/pipes
            while (romReference[doorTableLocation + index] != 0x00)
            {
                romIndex = doorTableLocation + index;
                
                if (romReference[romIndex] == 0x01 && romReference[romIndex + 6] == 0x00) // Ignore portal
                {
                    index += 12;
                    continue;
                }

                roomMax = Math.Max(roomMax, romReference[romIndex + 1]); // Get the room count, based on the largest room number
                roomDoorIsIn.Add(romReference[romIndex + 1]); // Add room index for door for future reference

                switch (romReference[romIndex])
                {
                    case 0x01:
                        type = PathType.Door;
                        break;
                    case 0x02:
                        byte x = romReference[romIndex + 7], y = romReference[romIndex + 8]; // Get x/y offset to detect type of door

                        if (y == 0 && x != 0)
                            type = x > 128 ? PathType.HallRight : PathType.HallLeft;
                        else
                            type = y > 128 ? PathType.Ceiling : PathType.Pit;

                        break;
                    case 0x03:
                        type = PathType.PipeBottom;
                        break;
                }

                path.Add(new PathNode(romIndex, type, romReference[romIndex + 6] - 1));

                index += 12;
            }

            rooms = new RoomNode[roomMax + 1];
            string[] substring;

            //TODO: Add subrooms to each room, and put doorways in those subrooms.

            for (int i = 0; i <= roomMax; i++) // Foreach room
            {
                RoomNode roomNew = new RoomNode();
                roomNew.subrooms = new List<RoomSubsection>(new RoomSubsection[] { new RoomSubsection(roomNew) });

                List<PathNode> fullnodeList = new List<PathNode>();
                substring = file[fileOffset + i].Split(',');

                index = 0;
                // Take all the doors in the room and add them to their proper place
                for (int j = 0; j < roomDoorIsIn.Count; j++)
                {
                    if (roomDoorIsIn[j] == i)
                    {
                        int subroom = int.Parse(substring[index++]); // Get the subroom door needs to be contained in

                        while (subroom >= roomNew.subrooms.Count) // Make sure that the subroom the door is contained in exists
                        {
                            roomNew.subrooms.Add(new RoomSubsection(roomNew));
                        }

                        roomNew.subrooms[subroom].doorWays.Add(path[j]); // Add door to subroom
                        path[j].subroom = roomNew.subrooms[subroom];
                    }
                }

                rooms[i] = roomNew;
            }

            fileOffset += roomMax + 1;

            while (file[fileOffset] != "" && file[fileOffset][0] != '/')
            {
                substring = file[fileOffset].Split(',');

                if (substring[0] == "C")
                {
                    path[int.Parse(substring[1])].pathType = (PathType)(int.Parse(substring[2]));
                }
                else if (substring[0] == "B")
                {
                    path[int.Parse(substring[1])].blockType = (PathBlockType)(int.Parse(substring[2]));
                }
                else if (substring[0] == "S")
                {
                    // Take the room from the second value, find the subroom from the third value, and add a connection to the subroom in the same main room at the index at the fourth value
                    rooms[int.Parse(substring[1])].subrooms[int.Parse(substring[2])].connections.Add(new SubroomConnection((PathBlockType)int.Parse(substring[4]), rooms[int.Parse(substring[1])].subrooms[int.Parse(substring[3])]));
                }
                fileOffset++;
            }
        }

        public void CreatePath(Random rng, ref byte[] rom)
        {
            List<PathNode> test = new List<PathNode>(path);

            bool isSafe = false;

            while (test.Count > 0)
            {
                int randomDoor = rng.Next(test.Count - 1) + 1;

                if ((int)test[0].pathType == -(int)(test[randomDoor].pathType))
                {
                    if (test[0].connectedNodeIndex >= 0)
                    {
                        test[0].connectedNodeIndex = path.IndexOf(test[randomDoor]);
                    }
                    if (test[randomDoor].connectedNodeIndex >= 0)
                    {
                        test[randomDoor].connectedNodeIndex = path.IndexOf(test[0]);
                    }
                    test.RemoveAt(randomDoor);
                    test.RemoveAt(0);
                }

                foreach (PathNode node in path)
                {
                    rom[node.doorIndex + 6] = (byte)(node.connectedNodeIndex + 1);
                }

                if (test.Count == 0) // After all rooms have been randomized
                {
                    LinkedList<RoomSubsection> roomPath = new LinkedList<RoomSubsection>();
                    List<PathNode> fixedPaths = new List<PathNode>();
                    PathNode issue = null;

                    isSafe = CheckPathSoftlocks(rooms[0].subrooms[0], roomPath, fixedPaths, ref issue);
                    
                    if (isSafe)
                    {
                        List<RoomSubsection> fullList = new List<RoomSubsection>();
                        foreach (RoomNode r in rooms)
                        {
                            fullList.AddRange(r.subrooms);
                        }
                        CheckPathOneWeb(rooms[0].subrooms[0], roomPath, fullList);

                        if (fullList.Count > 0)
                        {
                            isSafe = false;
                            issue = fullList[0].doorWays[0];
                        }
                    }

                    if (!isSafe)
                    {
                        test.Add(issue);
                        test.Add(path[issue.connectedNodeIndex]);
                        while (true)
                        {
                            randomDoor = rng.Next(0, path.Count);
                            if (randomDoor != path.IndexOf(issue) && randomDoor != issue.connectedNodeIndex && Math.Abs((int)path[randomDoor].pathType) == Math.Abs((int)issue.pathType))
                            {
                                test.Add(path[randomDoor]);
                                test.Add(path[path[randomDoor].connectedNodeIndex]);
                                break;
                            }
                        }
                    }


                }
            }

            //  Issue:
            //      Blocked pathways are being picked. (room that was meant to be going left to right doesn't work as the right side is covered by a breakable block which you spawn in)
            //  Fix?
            //      Have each path be given a block type, and make code to check from the portal that everything works.
            //      
        }

        public bool CheckPathSoftlocks(RoomSubsection roomSub, LinkedList<RoomSubsection> tracingPath, List<PathNode> safe, ref PathNode issue)
        {
            bool retVal = true; // Boolean on whether path is still safe

            if (tracingPath.Contains(roomSub))
                return true;
            
            tracingPath.AddLast(roomSub); // Add subroom to not retrace steps
            
            foreach (PathNode n in roomSub.doorWays) // Check each path if safe
            {
                int currentIndex = path.IndexOf(n);

                if (n.blockType != PathBlockType.Impassable) // If the pathway isn't impassable and the room hasn't been on the path already, 
                {
                    if (n.blockType == PathBlockType.BreakBlock_SoftLock)
                    {
                        safe.Add(n);
                    }
                    if (!safe.Contains(path[n.connectedNodeIndex]) && path[n.connectedNodeIndex].blockType == PathBlockType.BreakBlock_SoftLock)
                    {
                        retVal = false;
                        issue = n;
                        break;
                    }

                    if (!CheckPathSoftlocks(path[n.connectedNodeIndex].subroom, tracingPath, safe, ref issue))
                        retVal = false;

                    foreach (SubroomConnection sub in n.subroom.connections)
                    {
                        if (!tracingPath.Contains(sub.nextRoom) && !CheckPathSoftlocks(sub.nextRoom, tracingPath, safe, ref issue))
                        {
                            retVal = false;
                            break;
                        }
                    }
                }
                if (!retVal) break;
            }

            tracingPath.Remove(roomSub);

            return retVal;
        }
        public void CheckPathOneWeb(RoomSubsection roomSub, LinkedList<RoomSubsection> tracingPath, List<RoomSubsection> hasntFound)
        {
            if (tracingPath.Contains(roomSub))
                return;

            tracingPath.AddLast(roomSub); // Add subroom to not retrace steps
            if (hasntFound.Contains(roomSub))
                hasntFound.Remove(roomSub);

            foreach (PathNode n in roomSub.doorWays) // Check each path if safe
            {
                int currentIndex = path.IndexOf(n);

                if (n.blockType != PathBlockType.Impassable) // If the pathway isn't impassable and the room hasn't been on the path already, 
                {
                    CheckPathOneWeb(path[n.connectedNodeIndex].subroom, tracingPath, hasntFound);

                    foreach (SubroomConnection sub in n.subroom.connections)
                    {
                        if (!tracingPath.Contains(sub.nextRoom))
                        {
                            CheckPathOneWeb(sub.nextRoom, tracingPath, hasntFound);
                            break;
                        }
                    }
                }
            }

            tracingPath.Remove(roomSub);
        }
    }

    struct SubroomConnection
    {
        public PathBlockType issue;
        public RoomSubsection nextRoom;

        public SubroomConnection(PathBlockType block, RoomSubsection sub)
        {
            issue = block;
            nextRoom = sub;
        }
    }
    enum PathType
    {
        Door = 0,
        HallLeft = 1,
        HallRight = -1,
        Pit = 2,
        Ceiling = -2,
        PipeTop = 3,
        PipeBottom = -3,
    }
    enum PathBlockType
    {
        None = 0,
        FrogSwitchEnabled = 1,
        FrogSwitchDisabled = 2,
        RedToggle = 4,
        PurpleToggle = 8,
        GreenToggle = 16,
        BigBoardBlocks = 32,
        Impassable = 65536,
        BreakBlock_SoftLock = -1
    }
    class PathNode
    {
        public PathType pathType;
        public PathBlockType blockType;
        public int connectedNodeIndex;
        public RoomSubsection subroom;

        public int doorIndex;

        public PathNode(int index, PathType type, int _nodeIndex)
        {
            pathType = type;
            doorIndex = index;
            connectedNodeIndex = _nodeIndex;
        }
    }
    class RoomNode
    {
        public List<RoomSubsection> subrooms;

        public RoomNode(params RoomSubsection[] rooms)
        {
            subrooms = new List<RoomSubsection>(rooms);
        }
    }
    class RoomSubsection
    {
        public RoomNode parentRoom;
        public List<PathNode> doorWays;
        public List<SubroomConnection> connections;

        public RoomSubsection(RoomNode room)
        {
            parentRoom = room;
            doorWays = new List<PathNode>();
            connections = new List<SubroomConnection>();
        }
    }
}