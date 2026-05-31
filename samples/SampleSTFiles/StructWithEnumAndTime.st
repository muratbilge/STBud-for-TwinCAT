TYPE ST_AlarmConfig :
STRUCT
    priority:eAlarmPriority;
    message:STRING[128];
    active:BOOL;
    timestamp:TIME;
    count:DINT;
END_STRUCT
END_TYPE