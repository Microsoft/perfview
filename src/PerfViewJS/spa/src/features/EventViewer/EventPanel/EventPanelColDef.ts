import { IColumn } from "@fluentui/react";

export const EventPanelColDef: IColumn[] = [
  {
    key: "eventName",
    name: "Event Name",
    fieldName: "eventName",
    minWidth: 510,
  },
  {
    key: "timestamp",
    name: "Time MSec",
    fieldName: "timestamp",
    minWidth: 80,
  },
  {
    key: "processName",
    name: "Process Name",
    fieldName: "processName",
    minWidth: 100,
  },
  {
    key: "hasStack",
    name: "Has Stack",
    fieldName: "hasStack",
    minWidth: 80,
  },
  {
    key: "rest",
    name: "Rest",
    fieldName: "rest",
    minWidth: 1200,
  },
];
