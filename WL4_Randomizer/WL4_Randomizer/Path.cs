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

                if (path[path.Count - 1].connectedNodeIndex == -1)
                {
                    path[path.Count - 1].blockType = PathBlockType.Impassable;
                }

                index += 12;
            }

            rooms = new RoomNode[roomMax + 1];
            string[] substring;
            

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

            List<int> remove = new List<int>();

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
                else if (substring[0] == "K")
                {
                    path[int.Parse(substring[1])].Exclude = true;
                }
                else if (substring[0] == "R")
                {
                    rooms[int.Parse(substring[1])].Exclude = true;
                }
                else if (substring[0] == "S")
                {
                    // Take the room from the second value, find the subroom from the third value, and add a connection to the subroom in the same main room at the index at the fourth value
                    rooms[int.Parse(substring[1])].subrooms[int.Parse(substring[2])].connections.Add(new SubroomConnection((PathBlockType)int.Parse(substring[4]), rooms[int.Parse(substring[1])].subrooms[int.Parse(substring[3])]));
                }
                fileOffset++;
            }

            if (hall == 1 && level == 0)
            {
                bool stop = false;
                for (int i = 0; i < path.Count; i++)
                {
                    if (path[i].Exclude)
                    {
                        foreach (RoomNode main in rooms)
                        {
                            foreach (RoomSubsection sub in main.subrooms)
                            {
                                sub.doorWays.Remove(path[i]);
                            }
                        }
                    }
                }
            }

            roomExclusive = new List<RoomNode>(rooms);
            for (int i = 0; i < rooms.Length; i++)
            {
                if (path[i].Exclude)
                {
                    roomExclusive.Remove(rooms[i]);
                }
            }

            rooms[0].subrooms[0].itemsContained |= ItemFound.Portal;
        }

        List<RoomNode> roomExclusive;
        List<PathNode> pathExclusive;
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
    enum ItemFound
    {
        None = 0,
        Portal = 1,
        Frog = 2,
        Keyzer = 4,
        Gem1 = 8,
        Gem2 = 16,
        Gem3 = 32,
        Gem4 = 64
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
        FrogSwitchPre = 1,
        FrogSwitchPost = 2,
        RedToggle = 4,
        PurpleToggle = 8,
        GreenToggle = 16,
        BigBoardBlocks = 32,
        Zip = 64,

        Impassable = 65536,
        BreakBlock_SoftLock = 128
    }
    class PathNode
    {
        public PathType pathType;
        public PathBlockType blockType;
        public int connectedNodeIndex;
        public RoomSubsection subroom;
        public bool Exclude = false;

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
        public int EntityListIndex;
        public List<RoomSubsection> subrooms;
        public bool Exclude = false;

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
        public ItemFound itemsContained;

        public RoomSubsection(RoomNode room)
        {
            parentRoom = room;
            doorWays = new List<PathNode>();
            connections = new List<SubroomConnection>();
        }
    }
}
