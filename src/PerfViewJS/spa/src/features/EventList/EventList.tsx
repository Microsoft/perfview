import React, { useEffect, useState } from "react";
import { useHistory } from "react-router";
import { useDataFileContext } from "context/DataFileContext";
import { Col, Container, Row } from "react-grid-system";
import { CheckboxVisibility, DetailsList, IColumn, Text } from "@fluentui/react";
import { EventListColDef } from "./EventListColDef";
import base64url from "base64url";
import { TextLink } from "components/TextLink/TextLink";

interface Event {
  stackEventCount: number;
  eventId: string;
  name: string;
  eventCount: string;
  eventName: string;
}

const EventList = () => {
  const { dataFile } = useDataFileContext();
  const [events, setEvents] = useState<Event[]>([]);
  const history = useHistory();
  useEffect(() => {
    fetch("/api/eventlistos?filename=" + dataFile)
      .then((res) => res.json())
      .then((data) => {
        setEvents(data);
      });
  }, []);

  const renderItemColumn = (item?: Event, index?: number, column?: IColumn) => {
    if (column?.fieldName === "eventName" && item?.stackEventCount !== 0 && item) {
      return (
        <TextLink
          onClick={() => {
            history.push(
              `/ui/stackviewer/processchooser/${dataFile}/${item.eventId}/${base64url.encode(item.eventName)}`
            );
          }}
          content={item?.eventName || ""}
        />
      );
    } else {
      //? everything is optional..
      // eslint-disable-next-line @typescript-eslint/ban-ts-comment
      //@ts-ignore
      return item[column.fieldName];
    }
  };

  return (
    <Container>
      <Row>
        <Col>
          <Text variant={"xLarge"}>Stack Viewer</Text>
        </Col>
      </Row>
      <Row>
        <DetailsList
          checkboxVisibility={CheckboxVisibility.hidden}
          setKey={"key"}
          compact={true}
          items={events}
          columns={EventListColDef}
          onRenderItemColumn={renderItemColumn}
        />
      </Row>
    </Container>
  );
};

export { EventList };
