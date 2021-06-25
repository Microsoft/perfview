import React from "react";
import { Col, Container, Row } from "react-grid-system";

import { NavMenu } from "../features/NavMenu/NavMenu";

interface ILayoutProps {
  children: React.ReactNode;
}

const Layout = (props: ILayoutProps) => {
  const { children } = props;
  return (
    <Container fluid style={{ margin: 0 }}>
      <Row style={{ height: "100%", minHeight: "100vh" }}>
        <Col xs={3} md={2} lg={2}>
          <NavMenu />
        </Col>
        <Col xs={9} md={10} lg={10}>
          {children}
        </Col>
      </Row>
    </Container>
  );
};

export { Layout };
