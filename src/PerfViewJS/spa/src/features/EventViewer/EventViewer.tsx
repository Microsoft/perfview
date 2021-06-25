import React, { useEffect, useState } from "react";
import {
  DetailsList,
  DetailsListLayoutMode,
  IColumn,
  IObjectWithKey,
  DefaultButton,
  Selection,
  SelectionMode,
  TextField,
  Text,
  SpinButton,
  Position,
  ISpinButtonStyles,
  Stack,
  IStackTokens,
  IButtonStyles,
  getTheme,
} from "@fluentui/react";
import { useDataFileContext } from "context/DataFileContext";
import { Col, Container, Row } from "react-grid-system";
import { useBoolean } from "@fluentui/react-hooks";
import { EventPanel } from "./EventPanel/EventPanel";

//SpinButton only allows string
const defaultEventCount = "1000";
const defaultStartTime = "0.000";
const theme = getTheme();

interface IEventType {
  eventCount: number;
  eventId: number;
  eventName: string;
  stackEventCount: number;
}

const columns: IColumn[] = [
  {
    key: "column1",
    name: "Event Name",
    fieldName: "name",
    minWidth: 510,
  },
];

const detailsListStyles = {
  root: {
    cursor: "pointer",
    paddingTop: 20,
  },
};

//?azure theme spin button is different from TextField with labels..
const spinButtonStyles: Partial<ISpinButtonStyles> = {
  labelWrapper: { height: 27 },
  spinButtonWrapper: { height: 24 },
};

const buttonStyles: IButtonStyles = {
  root: {
    padding: 20,
    background: theme.semanticColors.primaryButtonBackground,
    color: theme.semanticColors.primaryButtonText,
  },
};

const stackTokens: IStackTokens = { childrenGap: 40, padding: "10px 0 0 0" };

const transformEventTypeToDetailListItems = (items: IEventType[]) =>
  items.map((item) => ({
    key: item.eventId,
    name: item.eventName,
    value: item.eventId,
  }));

const EventViewer: React.FC = () => {
  const { dataFile } = useDataFileContext();
  const [endTimeRelativeMSec, setEndTimeRelativeMSec] = useState<string>("");
  const [startTimeRelativeMSec, setStartTimeRelativeMSec] = useState<string>(defaultStartTime);
  const [selectedEvents, setSelectedEvents] = useState<string>("");
  const [regex, setRegex] = useState<string>("");
  const [maxEventCount, setMaxEventCount] = useState<number>(parseInt(defaultEventCount, 10));
  const [eventTypes, setEventTypes] = useState<IEventType[]>([]);
  const [filteredEventTypes, setFilteredEventTypes] = useState<IObjectWithKey[]>([]);

  const [isOpen, { setTrue: openPanel, setFalse: dismissPanel }] = useBoolean(false);

  useEffect(() => {
    fetch(`/api/traceinfo?filename=${dataFile}`)
      .then((res) => res.json())
      .then((data) => {
        setEndTimeRelativeMSec(data.endTimeRelativeMSec);
      });
    fetch(`/api/eventliston?filename=${dataFile}`)
      .then((res) => res.json())
      .then((data) => {
        setEventTypes(data);
        setFilteredEventTypes(transformEventTypeToDetailListItems(data));
      });
  }, [dataFile]);

  const selection = new Selection({
    onSelectionChanged: () => {
      if (selection.getSelectedCount() > 0) {
        const selectedItems = selection.getSelection();
        //create a csv of selected event ids
        setSelectedEvents(selectedItems.map((item) => item.key).join(","));
      }
    },
  });

  const onFilterChange = (ev: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, text: string | undefined) => {
    //https://developer.microsoft.com/en-us/fluentui#/controls/web/detailslist/compact
    //! bug when check all item & apply filter, same behavior on Fluent-ui site
    const filteredEvents = text ? eventTypes.filter((ev) => ev.eventName.toLowerCase().indexOf(text) > -1) : eventTypes;
    setFilteredEventTypes(transformEventTypeToDetailListItems(filteredEvents));
  };

  const onRegexChange = (ev: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, text: string | undefined) => {
    if (text) setRegex(text);
  };

  const onValidate = (value: string): string | void => {
    return Number.isInteger(parseInt(value)) ? value : defaultEventCount;
  };

  const onMaxEventChange = (event: React.SyntheticEvent<HTMLElement>, newValue?: string) => {
    if (newValue) setMaxEventCount(parseInt(newValue, 10));
  };

  const onStartTimeChange = (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string) => {
    if (newValue) setStartTimeRelativeMSec(newValue);
  };

  const onEndTimeChange = (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string) => {
    if (newValue) setEndTimeRelativeMSec(newValue);
  };

  return (
    <Container>
      <Row>
        <Col>
          <Text variant={"xLarge"}>Event Viewer</Text>
        </Col>
      </Row>
      <Row>
        <Col sm={6} md={4} lg={3}>
          <TextField
            label="Start Time (Relative MSec)"
            value={startTimeRelativeMSec}
            onChange={onStartTimeChange}
          ></TextField>
          <TextField
            label="End Time (Relative MSec)"
            value={endTimeRelativeMSec}
            onChange={onEndTimeChange}
          ></TextField>
        </Col>
        <Col sm={6} md={4} lg={3}>
          <SpinButton
            label="Maximum Event Count"
            labelPosition={Position.top}
            defaultValue={defaultEventCount}
            onChange={onMaxEventChange}
            min={1}
            max={100000}
            step={1000}
            onValidate={onValidate}
            styles={spinButtonStyles}
          />
          <TextField label="Text Filter (.NET Regex)" onChange={onRegexChange} value={regex}></TextField>
        </Col>
      </Row>
      <Row>
        <Col sm={12} md={8} lg={6}>
          <TextField label="Event Type Filter (the list below only)" onChange={onFilterChange}></TextField>
          <Stack tokens={stackTokens}>
            <DefaultButton styles={buttonStyles} onClick={openPanel}>
              Load Events
            </DefaultButton>
          </Stack>
        </Col>
      </Row>
      <Row>
        <Col>
          <DetailsList
            setKey={"key"}
            compact={true}
            items={filteredEventTypes}
            styles={detailsListStyles}
            columns={columns}
            selection={selection}
            selectionMode={SelectionMode.multiple}
            layoutMode={DetailsListLayoutMode.justified}
            selectionPreservedOnEmptyClick={true}
          />
        </Col>
      </Row>
      <EventPanel
        isOpen={isOpen}
        start={startTimeRelativeMSec}
        end={endTimeRelativeMSec}
        maxEventCount={maxEventCount}
        selectedEvents={selectedEvents}
        textFilter={regex}
        dismissPanel={dismissPanel}
      />
    </Container>
  );
};

export { EventViewer };
