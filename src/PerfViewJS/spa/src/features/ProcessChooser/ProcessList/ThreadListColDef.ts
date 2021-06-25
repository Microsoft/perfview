import { IColumn } from "@fluentui/react";

/*
cpuMsec: 0
endTime: "9999-12-31T23:59:59.9999999+08:00"
endTimeRelativeMSec: 9223368406851.713
startTime: "2021-05-20T10:57:11.3149367+08:00"
startTimeRelativeMSec: 0
threadId: 218
threadIndex: 0
*/
export const ThreadListColDef: IColumn[] = [
  {
    key: "Thread Id",
    name: "Thread Id",
    fieldName: "threadId",
    minWidth: 100,
  },
  {
    key: "Start Time",
    name: "Start Time",
    fieldName: "startTime",
    minWidth: 200,
  },
  {
    key: "Start Time Relative MSec",
    name: "Start Time Relative MSec",
    fieldName: "startTimeRelativeMSec",
    minWidth: 200,
  },
  {
    key: "End Time",
    name: "End Time",
    fieldName: "endTime",
    minWidth: 200,
  },
  {
    key: "End Time Relative MSec",
    name: "End Time Relative MSec",
    fieldName: "endTimeRelativeMSec",
    minWidth: 200,
  },

  {
    key: "CPU Milliseconds",
    name: "CPU Milliseconds",
    fieldName: "cpuMsec",
    minWidth: 300,
  },
];
