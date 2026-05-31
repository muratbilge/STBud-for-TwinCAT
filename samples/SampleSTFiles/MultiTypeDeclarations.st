TYPE eRobotMode :
(Manual, Auto, Jog, Reference, Error := 99)
END_TYPE

TYPE ST_AxisConfig :
STRUCT
    axisName:STRING[16];
    minPos:REAL;
    maxPos:REAL;
    maxVel:REAL;
    maxAcc:REAL;
    encoderResolution:DINT;
    inverted:BOOL;
END_STRUCT
END_TYPE