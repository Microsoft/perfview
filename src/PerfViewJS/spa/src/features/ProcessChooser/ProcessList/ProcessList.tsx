import React, { useEffect, useState } from "react";
import { useDataFileContext } from "context/DataFileContext";
import { Process } from "common/Interfaces";
import { CheckboxVisibility, DetailsList, IColumn, Text } from "@fluentui/react";
import { Col, Container, Row } from "react-grid-system";
import { ProcessListColDef } from "./ProcessListColDef";
import { TextLink } from "components/TextLink/TextLink";
import { useHistory } from "react-router";

interface IProcessListProps {
  processIndex: number;
}

export const ProcessList: React.FC<IProcessListProps> = (props) => {
  const { processIndex } = props;
  const [processes, setProcesses] = useState<Process[]>([]);
  const { dataFile } = useDataFileContext();
  const history = useHistory();

  useEffect(() => {
    fetch(`/api/processchooser?filename=${dataFile}`)
      .then((res) => res.json())
      .then((processes: Process[]) => {
        if (processIndex) {
          processes = processes.filter((process) => process.id === processIndex);
        }
        setProcesses(processes.filter((process) => process.id !== -1));
      });
  }, [dataFile]);

  const renderItemColumn = (item?: Process, index?: number, column?: IColumn) => {
    if (column?.fieldName === "name") {
      {
        return item?.processId === 0 ? (
          "Idle"
        ) : item?.processId === 4 ? (
          "System"
        ) : (
          <TextLink
            onClick={() => history.push(`/ui/processInfo/${dataFile}/${item?.id}`)}
            content={item?.name || ""}
          />
        );
      }
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
          <Text variant={"xLarge"}>Process List</Text>
        </Col>
      </Row>
      <Row>
        <Col>
          <DetailsList
            checkboxVisibility={CheckboxVisibility.hidden}
            setKey={"key"}
            compact={true}
            items={processes}
            columns={ProcessListColDef}
            onRenderItemColumn={renderItemColumn}
          />
        </Col>
      </Row>
    </Container>
  );
};
