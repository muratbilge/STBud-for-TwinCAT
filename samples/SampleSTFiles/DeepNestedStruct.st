TYPE ST_RobotArm :
STRUCT
    joints:ARRAY[1..6] OF ST_AxisConfig;
    toolOffset:ST_Point;
    speedOverride:REAL;
    accelOverride:REAL;
    mode:eRobotMode;
    errorCodes:ARRAY[1..10] OF DINT;
    isConnected:BOOL;
    lastUpdate:DT;
END_STRUCT
END_TYPE