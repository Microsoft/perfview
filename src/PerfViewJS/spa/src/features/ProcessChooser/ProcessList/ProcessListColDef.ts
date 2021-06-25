import { IColumn } from "@fluentui/react";

export const ProcessListColDef: IColumn[] = [
  {
    key: "Process Name",
    name: "Process Name",
    fieldName: "name",
    minWidth: 300,
  },
  {
    key: "Process Id",
    name: "Process Id",
    fieldName: "processId",
    minWidth: 100,
  },
  {
    key: "Parent Id",
    name: "Parent Id",
    fieldName: "parentId",
    minWidth: 100,
  },
  {
    key: "CPU Milliseconds",
    name: "CPU Milliseconds",
    fieldName: "cpumSec",
    minWidth: 100,
  },
  {
    key: "Command Line",
    name: "Command Line",
    fieldName: "commandLine",
    minWidth: 100,
  },
];
