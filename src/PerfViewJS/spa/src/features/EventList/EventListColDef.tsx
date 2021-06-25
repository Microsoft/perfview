import { IColumn } from "@fluentui/react";

export const EventListColDef: IColumn[] = [
  {
    key: "eventName",
    name: "Event Name",
    isResizable: true,
    fieldName: "eventName",
    minWidth: 600,
  },
  {
    key: "stackEventCount",
    name: "Stack Count",
    fieldName: "stackEventCount",
    minWidth: 200,
  },
  {
    key: "eventCount",
    name: "Event Count",
    fieldName: "eventCount",
    minWidth: 200,
  },
];
