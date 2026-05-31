TYPE ST_MotorCfg :
STRUCT
    name:STRING[32];
    speed:REAL;
    position:ST_Point;
    state:eMotorState;
    limits:ARRAY[0..3] OF REAL;
    enabled:BOOL;
END_STRUCT
END_TYPE